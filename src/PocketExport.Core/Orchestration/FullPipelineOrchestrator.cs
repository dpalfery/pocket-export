namespace PocketExport.Core.Orchestration;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Metadata;
using PocketExport.Core.Ml;
using PocketExport.Core.Models;
using PocketExport.Core.Pocket;
using PocketExport.Core.Routing;
using PocketExport.Core.Speakers;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using PocketExport.Core.Taxonomy;

/// <summary>
/// Coordinates the end-to-end recording processing pipeline across sync, ML transcription,
/// voicebank matching, taxonomy classification, metadata tagging, routing, summarization,
/// and ledger/SQLite projection.
/// </summary>
public sealed class FullPipelineOrchestrator : IFullPipelineOrchestrator
{
    private const double DefaultConfidenceThreshold = 0.75;

    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly PocketSyncEngine _syncEngine;
    private readonly IAudioWorkerClient _audioWorkerClient;
    private readonly ISpeakerVoicebankService _speakerVoicebankService;
    private readonly ITaxonomyClassifier _taxonomyClassifier;
    private readonly IAudioContainerTagger _audioContainerTagger;
    private readonly IVaultFolderRouter _vaultFolderRouter;
    private readonly ITemplateSummarizer _templateSummarizer;
    private readonly LedgerProjectionService _ledgerProjectionService;
    private readonly ILogger<FullPipelineOrchestrator>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FullPipelineOrchestrator"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">Vault path normalization and resolution service.</param>
    /// <param name="ledgerManager">Atomic ledger persistence manager.</param>
    /// <param name="pocketApiClient">Client for HeyPocket API interactions.</param>
    /// <param name="audioWorkerClient">Worker client for transcription and diarization.</param>
    /// <param name="speakerVoicebankService">Biometric voicebank matching and enrollment service.</param>
    /// <param name="taxonomyClassifier">Taxonomy classifier using LLM inference.</param>
    /// <param name="audioContainerTagger">Audio container tagger via TagLibSharp.</param>
    /// <param name="vaultFolderRouter">Folder router for classified markdown transcripts.</param>
    /// <param name="templateSummarizer">Template-based markdown summarizer.</param>
    /// <param name="ledgerProjectionService">Projection service synchronizing ledger manifests to SQLite.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="pocketSyncEngine">Optional pre-configured Pocket sync engine.</param>
    public FullPipelineOrchestrator(
        IVaultPathResolver vaultPathResolver,
        ILedgerManager ledgerManager,
        IPocketApiClient pocketApiClient,
        IAudioWorkerClient audioWorkerClient,
        ISpeakerVoicebankService speakerVoicebankService,
        ITaxonomyClassifier taxonomyClassifier,
        IAudioContainerTagger audioContainerTagger,
        IVaultFolderRouter vaultFolderRouter,
        ITemplateSummarizer templateSummarizer,
        LedgerProjectionService ledgerProjectionService,
        ILogger<FullPipelineOrchestrator>? logger = null,
        PocketSyncEngine? pocketSyncEngine = null)
    {
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        ArgumentNullException.ThrowIfNull(pocketApiClient);
        _audioWorkerClient = audioWorkerClient ?? throw new ArgumentNullException(nameof(audioWorkerClient));
        _speakerVoicebankService = speakerVoicebankService ?? throw new ArgumentNullException(nameof(speakerVoicebankService));
        _taxonomyClassifier = taxonomyClassifier ?? throw new ArgumentNullException(nameof(taxonomyClassifier));
        _audioContainerTagger = audioContainerTagger ?? throw new ArgumentNullException(nameof(audioContainerTagger));
        _vaultFolderRouter = vaultFolderRouter ?? throw new ArgumentNullException(nameof(vaultFolderRouter));
        _templateSummarizer = templateSummarizer ?? throw new ArgumentNullException(nameof(templateSummarizer));
        _ledgerProjectionService = ledgerProjectionService ?? throw new ArgumentNullException(nameof(ledgerProjectionService));
        _logger = logger;
        _syncEngine = pocketSyncEngine ?? new PocketSyncEngine(pocketApiClient, vaultPathResolver, ledgerManager);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FullPipelineExecutionResult>> ExecuteFullPipelineAsync(CancellationToken cancellationToken = default)
    {
        var stagedRecordings = await _syncEngine.SyncRecordingsAsync(cancellationToken);
        var results = new List<FullPipelineExecutionResult>();

        if (stagedRecordings.Count == 0)
        {
            return results;
        }

        var enrolledProfiles = await _ledgerManager.GetAllSpeakerProfilesAsync(cancellationToken);

        foreach (var staged in stagedRecordings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recordingId = staged.Id;
            var platformAudioPath = _vaultPathResolver.ResolvePlatformPath(staged.StagingAudioPath);
            var stagingMetadataPosixPath = staged.PocketMetadataPath ?? $"staging/{recordingId}.pocket.json";
            var platformMetadataPath = _vaultPathResolver.ResolvePlatformPath(stagingMetadataPosixPath);

            // 1. Transcription & diarization via AudioWorkerClient
            var transcription = await _audioWorkerClient.TranscribeAsync(platformAudioPath, cancellationToken);

            // 2. Extract cloud hints from pocket metadata if available
            var cloudSpeakerHints = await TryExtractCloudSpeakerHintsAsync(platformMetadataPath, cancellationToken);

            // 3. Biometric speaker matching via SpeakerVoicebankService
            var recordingSpeakers = await ResolveSpeakersAsync(transcription, enrolledProfiles, cloudSpeakerHints, cancellationToken);

            var speakerDisplayNames = recordingSpeakers
                .Select(s => s.DisplayName ?? s.ClusterId)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            // 4. Taxonomy classification via TaxonomyClassifier
            var pocketSummary = await TryExtractPocketSummaryAsync(platformMetadataPath, staged.Title, cancellationToken);
            var classification = await _taxonomyClassifier.ClassifyRecordingAsync(
                transcription.Text,
                speakerDisplayNames,
                pocketSummary,
                cancellationToken);

            // 5. Container metadata tagging via AudioContainerTagger
            await _audioContainerTagger.TagAudioFileAsync(
                platformAudioPath,
                staged.Title,
                speakerDisplayNames,
                classification.Domain,
                classification.Rationale ?? classification.Initiative ?? string.Empty,
                cancellationToken);

            // 6. Template summarization via TemplateSummarizer
            var summaryMarkdown = await _templateSummarizer.GenerateSummaryAsync(
                classification.TemplateId,
                transcription.Text,
                speakerDisplayNames,
                classification,
                cancellationToken);

            // 7. Route transcript & summary destination path via VaultFolderRouter
            var titleSlug = _vaultPathResolver.SanitizePathSegment(
                staged.Title.Trim().ToLowerInvariant().Replace(' ', '-'));

            var routedSummaryPosixPath = _vaultFolderRouter.DetermineRoutedPath(
                classification,
                recordingId,
                staged.CreatedAt,
                titleSlug);

            var routedSummaryPlatformPath = _vaultPathResolver.ResolvePlatformPath(routedSummaryPosixPath);
            var routedDir = Path.GetDirectoryName(routedSummaryPlatformPath);
            if (!string.IsNullOrEmpty(routedDir))
            {
                Directory.CreateDirectory(routedDir);
            }

            await File.WriteAllTextAsync(routedSummaryPlatformPath, summaryMarkdown, Encoding.UTF8, cancellationToken);

            // 8. Update RecordingManifest in ledger and project to SQLite
            var status = classification.Confidence >= DefaultConfidenceThreshold ? "Completed" : "NeedsReview";
            var updatedManifest = staged with
            {
                Status = status,
                RoutedSummaryPath = routedSummaryPosixPath,
                RoutedTranscriptPath = routedSummaryPosixPath,
                Classification = classification,
                Speakers = recordingSpeakers
            };

            await _ledgerManager.SaveRecordingManifestAsync(updatedManifest, cancellationToken);
            await _ledgerProjectionService.ProjectRecordingAsync(updatedManifest, cancellationToken);

            results.Add(new FullPipelineExecutionResult(
                RecordingId: recordingId,
                StagingAudioPlatformPath: platformAudioPath,
                StagingMetadataPlatformPath: platformMetadataPath,
                RoutedSummaryPlatformPath: routedSummaryPlatformPath,
                FinalManifest: updatedManifest));
        }

        return results;
    }

    private async Task<IReadOnlyList<RecordingSpeaker>> ResolveSpeakersAsync(
        TranscriptionResult transcription,
        IReadOnlyList<SpeakerProfile> enrolledProfiles,
        IReadOnlyList<string>? cloudSpeakerHints,
        CancellationToken cancellationToken)
    {
        _ = enrolledProfiles;

        if ((transcription.Segments == null || transcription.Segments.Count == 0) &&
            (transcription.Speakers == null || transcription.Speakers.Count == 0))
        {
            return [];
        }

        var speakersByCluster = new List<RecordingSpeaker>();
        var workerSpeakersById = (transcription.Speakers ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.ClusterId))
            .ToDictionary(s => s.ClusterId, s => s, StringComparer.OrdinalIgnoreCase);

        var clusterIds = (transcription.Segments ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Speaker))
            .Select(s => s.Speaker)
            .Concat(workerSpeakersById.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var clusterId in clusterIds)
        {
            var firstSeg = transcription.Segments?.FirstOrDefault(s => string.Equals(s.Speaker, clusterId, StringComparison.OrdinalIgnoreCase));
            workerSpeakersById.TryGetValue(clusterId, out var workerSpeaker);

            var soloSnippet = workerSpeaker?.SoloSnippet
                ?? (firstSeg is not null ? new SoloSnippetBounds(firstSeg.StartMs, firstSeg.EndMs) : new SoloSnippetBounds(0, 8000));

            var embedding = workerSpeaker?.EmbeddingVector is { Count: > 0 } vec
                ? vec.ToArray()
                : new float[512];

            var matchResult = await _speakerVoicebankService.MatchSpeakerAsync(
                embedding,
                cloudSpeakerHints,
                cancellationToken);

            speakersByCluster.Add(new RecordingSpeaker(
                ClusterId: clusterId,
                EnrolledSpeakerId: matchResult.SpeakerId,
                DisplayName: matchResult.SpeakerId is not null ? matchResult.DisplayName : clusterId,
                MatchConfidence: matchResult.Confidence,
                NeedsDisambiguation: matchResult.NeedsDisambiguation,
                SoloSnippet: soloSnippet,
                EmbeddingVector: embedding));
        }

        return speakersByCluster;
    }

    private static async Task<IReadOnlyList<string>?> TryExtractCloudSpeakerHintsAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("speakers", out var speakersProp) && speakersProp.ValueKind == JsonValueKind.Array)
            {
                var hints = new List<string>();
                foreach (var el in speakersProp.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                    {
                        hints.Add(s);
                    }
                    else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { } name)
                    {
                        hints.Add(name);
                    }
                }

                return hints.Count > 0 ? hints : null;
            }
        }
        catch
        {
            // Ignore parse errors on raw metadata
        }

        return null;
    }

    private static async Task<string> TryExtractPocketSummaryAsync(string metadataPath, string defaultSummary, CancellationToken cancellationToken)
    {
        if (File.Exists(metadataPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("summary", out var summaryProp) && summaryProp.GetString() is { } summary && !string.IsNullOrWhiteSpace(summary))
                {
                    return summary;
                }

                if (doc.RootElement.TryGetProperty("title", out var titleProp) && titleProp.GetString() is { } title && !string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
            catch
            {
                // Ignore parse errors on raw metadata
            }
        }

        return defaultSummary;
    }
}
