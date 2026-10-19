namespace PocketExport.Core.Ledger;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;

/// <summary>
/// Implements atomic ledger persistence, conflict-free updates, and concurrency retry semantics.
/// </summary>
public sealed class LedgerManager : ILedgerManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IVaultPathResolver _vaultPathResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="LedgerManager"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">Resolver for platform and vault-relative filesystem paths.</param>
    public LedgerManager(IVaultPathResolver vaultPathResolver)
    {
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
    }

    /// <inheritdoc />
    public Task SaveRecordingManifestAsync(RecordingManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);

        var relativePath = $".ledger/recordings/{manifest.Id}.json";
        return SaveWithRetryAsync(relativePath, manifest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RecordingManifest?> GetRecordingManifestAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var relativePath = $".ledger/recordings/{id}.json";
        var absolutePath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<RecordingManifest>(stream, JsonOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordingManifest>> GetAllRecordingManifestsAsync(CancellationToken cancellationToken = default)
    {
        var absoluteDir = _vaultPathResolver.ResolvePlatformPath(".ledger/recordings");
        if (!Directory.Exists(absoluteDir))
        {
            return [];
        }

        var files = Directory.GetFiles(absoluteDir, "*.json");
        var manifests = new List<RecordingManifest>(files.Length);

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var manifest = await GetRecordingManifestAsync(id, cancellationToken);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    /// <inheritdoc />
    public Task DeleteRecordingManifestAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var relativePath = $".ledger/recordings/{id}.json";
        var absolutePath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveSpeakerProfileAsync(SpeakerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);

        var relativePath = $".ledger/speakers/{profile.Id}.json";
        return SaveWithRetryAsync(relativePath, profile, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SpeakerProfile?> GetSpeakerProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var relativePath = $".ledger/speakers/{id}.json";
        var absolutePath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<SpeakerProfile>(stream, JsonOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpeakerProfile>> GetAllSpeakerProfilesAsync(CancellationToken cancellationToken = default)
    {
        var absoluteDir = _vaultPathResolver.ResolvePlatformPath(".ledger/speakers");
        if (!Directory.Exists(absoluteDir))
        {
            return [];
        }

        var files = Directory.GetFiles(absoluteDir, "*.json");
        var profiles = new List<SpeakerProfile>(files.Length);

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var profile = await GetSpeakerProfileAsync(id, cancellationToken);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    /// <inheritdoc />
    public Task DeleteSpeakerProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var relativePath = $".ledger/speakers/{id}.json";
        var absolutePath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private async Task SaveWithRetryAsync<T>(string relativePath, T data, CancellationToken cancellationToken)
    {
        var targetPath = _vaultPathResolver.ResolvePlatformPath(relativePath);
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);

        var stopwatch = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(5);
        var delay = TimeSpan.FromMilliseconds(50);
        var delayStep = TimeSpan.FromMilliseconds(50);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? tmpPath = null;

            try
            {
                if (File.Exists(targetPath))
                {
                    using var testStream = new FileStream(
                        targetPath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }

                tmpPath = Path.Combine(dir!, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
                File.Move(tmpPath, targetPath, overwrite: true);
                return;
            }
            catch (IOException)
            {
                if (stopwatch.Elapsed >= timeout)
                {
                    throw;
                }

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw;
                }

                var currentDelay = delay < remaining ? delay : remaining;
                await Task.Delay(currentDelay, cancellationToken);
                delay += delayStep;
            }
            finally
            {
                if (tmpPath is not null)
                {
                    try
                    {
                        if (File.Exists(tmpPath))
                        {
                            File.Delete(tmpPath);
                        }
                    }
                    catch
                    {
                        // Best effort cleanup of temporary write file
                    }
                }
            }
        }
    }
}
