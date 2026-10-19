namespace PocketExport.Core.Pocket;

using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using Polly;
using Polly.Retry;

/// <summary>
/// Synchronizes audio recordings and cloud metadata from Pocket AI into local vault storage and ledger manifests.
/// </summary>
public sealed class PocketSyncEngine
{
    private readonly IPocketApiClient _pocketApiClient;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly ILogger<PocketSyncEngine>? _logger;
    private readonly ResiliencePipeline _retryPipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PocketSyncEngine"/> class.
    /// </summary>
    /// <param name="pocketApiClient">Client for HeyPocket API interactions.</param>
    /// <param name="vaultPathResolver">Vault path normalization and resolution service.</param>
    /// <param name="ledgerManager">Atomic ledger persistence manager.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="retryPipeline">Optional Polly resilience pipeline for retrying transient operations.</param>
    public PocketSyncEngine(
        IPocketApiClient pocketApiClient,
        IVaultPathResolver vaultPathResolver,
        ILedgerManager ledgerManager,
        ILogger<PocketSyncEngine>? logger = null,
        ResiliencePipeline? retryPipeline = null)
    {
        _pocketApiClient = pocketApiClient ?? throw new ArgumentNullException(nameof(pocketApiClient));
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        _logger = logger;
        _retryPipeline = retryPipeline ?? CreateDefaultRetryPipeline();
    }

    /// <summary>
    /// Synchronizes available recordings from Pocket AI to the local staging directory and ledger.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of staged recording manifests.</returns>
    public async Task<IReadOnlyList<RecordingManifest>> SyncRecordingsAsync(CancellationToken cancellationToken = default)
    {
        var stagedManifests = new List<RecordingManifest>();

        var recordings = await _retryPipeline.ExecuteAsync(
            async ct => await _pocketApiClient.ListRecordingsAsync(ct),
            cancellationToken);

        if (recordings == null || recordings.Count == 0)
        {
            return stagedManifests;
        }

        foreach (var recording in recordings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var audioUri = await _retryPipeline.ExecuteAsync(
                async ct => await _pocketApiClient.GetAudioUrlAsync(recording.Id, ct),
                cancellationToken);

            string extension = Path.GetExtension(audioUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".m4a";
            }

            string stagingAudioPosixPath = $"staging/{recording.Id}{extension}";
            string stagingMetadataPosixPath = $"staging/{recording.Id}.pocket.json";

            string platformAudioPath = _vaultPathResolver.ResolvePlatformPath(stagingAudioPosixPath);
            string platformMetadataPath = _vaultPathResolver.ResolvePlatformPath(stagingMetadataPosixPath);

            var audioDir = Path.GetDirectoryName(platformAudioPath);
            if (!string.IsNullOrEmpty(audioDir))
            {
                Directory.CreateDirectory(audioDir);
            }

            var metadataDir = Path.GetDirectoryName(platformMetadataPath);
            if (!string.IsNullOrEmpty(metadataDir))
            {
                Directory.CreateDirectory(metadataDir);
            }

            try
            {
                await _retryPipeline.ExecuteAsync(async ct =>
                {
                    if (File.Exists(platformAudioPath))
                    {
                        File.Delete(platformAudioPath);
                    }

                    using var audioStream = await _pocketApiClient.DownloadAudioStreamAsync(audioUri, ct);

                    long? expectedLength = null;
                    try
                    {
                        expectedLength = audioStream.Length;
                    }
                    catch (NotSupportedException)
                    {
                        // Stream does not support Length property
                    }

                    await using (var fileStream = new FileStream(platformAudioPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await audioStream.CopyToAsync(fileStream, ct);
                        await fileStream.FlushAsync(ct);
                    }

                    if (expectedLength.HasValue && expectedLength.Value >= 0)
                    {
                        var fileInfo = new FileInfo(platformAudioPath);
                        if (fileInfo.Length != expectedLength.Value)
                        {
                            throw new InvalidDataException(
                                $"Content length mismatch for {recording.Id}: expected {expectedLength.Value} bytes, but wrote {fileInfo.Length} bytes.");
                        }
                    }
                }, cancellationToken);

                string rawDetailJson = await _retryPipeline.ExecuteAsync(
                    async ct => await _pocketApiClient.GetRawRecordingDetailJsonAsync(recording.Id, ct),
                    cancellationToken);

                await File.WriteAllTextAsync(platformMetadataPath, rawDetailJson, Encoding.UTF8, cancellationToken);

                var manifest = new RecordingManifest(
                    Id: recording.Id,
                    PocketId: recording.Id,
                    CreatedAt: recording.CreatedAt,
                    Title: recording.Title,
                    DurationSeconds: recording.DurationSeconds,
                    StagingAudioPath: stagingAudioPosixPath,
                    PocketMetadataPath: stagingMetadataPosixPath,
                    Status: "Staged");

                await _ledgerManager.SaveRecordingManifestAsync(manifest, cancellationToken);
                stagedManifests.Add(manifest);
            }
            catch
            {
                if (File.Exists(platformAudioPath))
                {
                    try
                    {
                        File.Delete(platformAudioPath);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }

                if (File.Exists(platformMetadataPath))
                {
                    try
                    {
                        File.Delete(platformMetadataPath);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }

                throw;
            }
        }

        return stagedManifests;
    }

    private static ResiliencePipeline CreateDefaultRetryPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ex =>
                    ex.StatusCode == HttpStatusCode.TooManyRequests ||
                    (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500)),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false
            })
            .Build();
    }
}
