using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PocketExport.Api.Models;
using PocketExport.Api.Services;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Ml;
using PocketExport.Core.Models;
using PocketExport.Core.Routing;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;

namespace PocketExport.Api.Controllers;

/// <summary>
/// API controller exposing operations for querying, retrieving, retagging, retranscribing, resummarizing,
/// and streaming audio snippets for recordings stored within the vault.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class RecordingsController : ControllerBase
{
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly IVaultFolderRouter _vaultFolderRouter;
    private readonly IAudioWorkerClient _audioWorkerClient;
    private readonly ITemplateSummarizer _templateSummarizer;
    private readonly IAudioSnippetStreamer _audioSnippetStreamer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingsController"/> class.
    /// </summary>
    /// <param name="ledgerManager">The ledger manager instance.</param>
    /// <param name="sqliteContext">The local SQLite index context.</param>
    /// <param name="vaultPathResolver">The vault path resolver instance.</param>
    /// <param name="vaultFolderRouter">The vault folder router instance.</param>
    /// <param name="audioWorkerClient">The audio ML worker client.</param>
    /// <param name="templateSummarizer">The template summarizer service.</param>
    /// <param name="audioSnippetStreamer">The audio snippet streamer service.</param>
    public RecordingsController(
        ILedgerManager ledgerManager,
        SqliteIndexContext sqliteContext,
        IVaultPathResolver vaultPathResolver,
        IVaultFolderRouter vaultFolderRouter,
        IAudioWorkerClient audioWorkerClient,
        ITemplateSummarizer templateSummarizer,
        IAudioSnippetStreamer audioSnippetStreamer)
    {
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        _sqliteContext = sqliteContext ?? throw new ArgumentNullException(nameof(sqliteContext));
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
        _vaultFolderRouter = vaultFolderRouter ?? throw new ArgumentNullException(nameof(vaultFolderRouter));
        _audioWorkerClient = audioWorkerClient ?? throw new ArgumentNullException(nameof(audioWorkerClient));
        _templateSummarizer = templateSummarizer ?? throw new ArgumentNullException(nameof(templateSummarizer));
        _audioSnippetStreamer = audioSnippetStreamer ?? throw new ArgumentNullException(nameof(audioSnippetStreamer));
    }

    /// <summary>
    /// Retrieves all recordings matching optional filter criteria.
    /// </summary>
    /// <param name="domain">Optional taxonomy domain filter.</param>
    /// <param name="customer">Optional customer filter.</param>
    /// <param name="status">Optional processing status filter.</param>
    /// <param name="search">Optional search term matching recording title.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A list of matching recording manifests.</returns>
    [HttpGet]
    public async Task<IActionResult> GetRecordingsAsync(
        [FromQuery] string? domain = null,
        [FromQuery] string? customer = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        HashSet<string>? indexedMatchingIds = null;
        try
        {
            await _sqliteContext.InitializeDatabaseAsync(cancellationToken);
            using var connection = _sqliteContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var totalInDb = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(1) FROM recordings;", cancellationToken: cancellationToken));

            if (totalInDb > 0)
            {
                const string sql = """
                    SELECT id
                    FROM recordings
                    WHERE (@Domain IS NULL OR LOWER(domain) = LOWER(@Domain))
                      AND (@Customer IS NULL OR LOWER(customer) = LOWER(@Customer))
                      AND (@Status IS NULL OR LOWER(status) = LOWER(@Status))
                      AND (@Search IS NULL OR LOWER(title) LIKE '%' || LOWER(@Search) || '%')
                    ORDER BY created_at DESC;
                    """;

                var ids = await connection.QueryAsync<string>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                            Customer = string.IsNullOrWhiteSpace(customer) ? null : customer,
                            Status = string.IsNullOrWhiteSpace(status) ? null : status,
                            Search = string.IsNullOrWhiteSpace(search) ? null : search
                        },
                        cancellationToken: cancellationToken));

                indexedMatchingIds = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fall back to ledger manifest scan if SQLite index is unavailable
        }

        if (indexedMatchingIds is not null)
        {
            var matchedManifests = new List<RecordingManifest>(indexedMatchingIds.Count);
            foreach (var recId in indexedMatchingIds)
            {
                var manifest = await _ledgerManager.GetRecordingManifestAsync(recId, cancellationToken);
                if (manifest is not null)
                {
                    matchedManifests.Add(manifest);
                }
            }

            return Ok(matchedManifests);
        }

        var manifests = await _ledgerManager.GetAllRecordingManifestsAsync(cancellationToken);
        IEnumerable<RecordingManifest> query = manifests;

        if (!string.IsNullOrWhiteSpace(domain))
        {
            query = query.Where(m => string.Equals(m.Classification?.Domain, domain, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(customer))
        {
            query = query.Where(m => string.Equals(m.Classification?.Customer, customer, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => string.Equals(m.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m => m.Title is not null && m.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(query.ToList());
    }

    /// <summary>
    /// Retrieves full details for a specific recording manifest by identifier.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The recording manifest if found; otherwise, 404 NotFound.</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRecordingByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var recording = await _ledgerManager.GetRecordingManifestAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }

        return Ok(recording);
    }

    /// <summary>
    /// Retags a recording with updated taxonomy classification metadata, moves routed files atomically, and updates the ledger.
    /// </summary>
    /// <param name="id">The recording identifier.</param>
    /// <param name="request">The retagging metadata request payload.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The updated recording manifest.</returns>
    [HttpPost("{id}/retag")]
    public async Task<IActionResult> RetagAsync(
        string id,
        [FromBody] RetagRecordingRequest request,
        CancellationToken cancellationToken = default)
    {
        var recording = await _ledgerManager.GetRecordingManifestAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }

        var newClassification = new ClassificationResult(
            Domain: request.Domain,
            Customer: request.Customer,
            Project: request.Project,
            Initiative: request.Initiative,
            Confidence: 1.0,
            TemplateId: request.TemplateId ?? recording.Classification?.TemplateId ?? "default",
            Rationale: "User retag");

        var titleSlug = _vaultPathResolver.SanitizePathSegment(
            recording.Title.Trim().ToLowerInvariant().Replace(' ', '-'));

        var newRoutedPath = _vaultFolderRouter.DetermineRoutedPath(
            newClassification,
            recording.Id,
            recording.CreatedAt,
            titleSlug);

        if (!string.IsNullOrWhiteSpace(recording.RoutedSummaryPath))
        {
            var oldAbsPath = _vaultPathResolver.ResolvePlatformPath(recording.RoutedSummaryPath);
            var newAbsPath = _vaultPathResolver.ResolvePlatformPath(newRoutedPath);

            if (System.IO.File.Exists(oldAbsPath) && !string.Equals(oldAbsPath, newAbsPath, StringComparison.OrdinalIgnoreCase))
            {
                var targetDir = Path.GetDirectoryName(newAbsPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                System.IO.File.Move(oldAbsPath, newAbsPath, overwrite: true);
            }
        }

        var updatedManifest = recording with
        {
            Classification = newClassification,
            RoutedSummaryPath = newRoutedPath
        };

        await _ledgerManager.SaveRecordingManifestAsync(updatedManifest, cancellationToken);

        try
        {
            using var connection = _sqliteContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            const string updateSql = """
                UPDATE recordings
                SET domain = @Domain,
                    customer = @Customer,
                    project = @Project,
                    initiative = @Initiative,
                    template_id = @TemplateId,
                    routed_summary_path = @RoutedSummaryPath,
                    updated_at = @UpdatedAt
                WHERE id = @Id;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    Id = recording.Id,
                    Domain = newClassification.Domain,
                    Customer = newClassification.Customer,
                    Project = newClassification.Project,
                    Initiative = newClassification.Initiative,
                    TemplateId = newClassification.TemplateId,
                    RoutedSummaryPath = newRoutedPath,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                },
                cancellationToken: cancellationToken));
        }
        catch
        {
            // Best-effort SQLite update
        }

        return Ok(updatedManifest);
    }

    /// <summary>
    /// Triggers re-transcription of the staging audio file using the local audio ML worker.
    /// </summary>
    /// <param name="id">The recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The updated transcription result.</returns>
    [HttpPost("{id}/retranscribe")]
    public async Task<IActionResult> RetranscribeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var recording = await _ledgerManager.GetRecordingManifestAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }

        var transcriptionResult = await _audioWorkerClient.TranscribeAsync(recording.StagingAudioPath, cancellationToken);

        if (!string.IsNullOrWhiteSpace(recording.RoutedTranscriptPath))
        {
            var absTranscriptPath = _vaultPathResolver.ResolvePlatformPath(recording.RoutedTranscriptPath);
            var dir = Path.GetDirectoryName(absTranscriptPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await System.IO.File.WriteAllTextAsync(absTranscriptPath, transcriptionResult.Text, cancellationToken);
        }

        return Ok(transcriptionResult);
    }

    /// <summary>
    /// Triggers structured markdown summary generation using the configured template summarizer.
    /// </summary>
    /// <param name="id">The recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The generated structured markdown summary.</returns>
    [HttpPost("{id}/resummarize")]
    public async Task<IActionResult> ResummarizeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var recording = await _ledgerManager.GetRecordingManifestAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }

        var transcriptText = string.Empty;
        if (!string.IsNullOrWhiteSpace(recording.RoutedTranscriptPath))
        {
            var absTranscriptPath = _vaultPathResolver.ResolvePlatformPath(recording.RoutedTranscriptPath);
            if (System.IO.File.Exists(absTranscriptPath))
            {
                transcriptText = await System.IO.File.ReadAllTextAsync(absTranscriptPath, cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            transcriptText = recording.Title;
        }

        var speakerNames = recording.Speakers?.Select(s => s.DisplayName ?? s.ClusterId).ToList();

        var summary = await _templateSummarizer.GenerateSummaryAsync(
            recording.Classification?.TemplateId,
            transcriptText,
            speakerNames,
            recording.Classification ?? new ClassificationResult("General", "General", "General", "General", 0.0, "default"),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(recording.RoutedSummaryPath))
        {
            var absSummaryPath = _vaultPathResolver.ResolvePlatformPath(recording.RoutedSummaryPath);
            var dir = Path.GetDirectoryName(absSummaryPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await System.IO.File.WriteAllTextAsync(absSummaryPath, summary, cancellationToken);
        }

        return Ok(new { Summary = summary });
    }

    /// <summary>
    /// Streams a discrete audio byte slice for a detected solo speaker snippet returning HTTP 206 Partial Content.
    /// </summary>
    /// <param name="id">The recording identifier.</param>
    /// <param name="speakerIndex">The zero-based index of the speaker in the recording manifest.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>HTTP 206 Partial Content with Content-Range byte stream.</returns>
    [HttpGet("{id}/speakers/{speakerIndex:int}/snippet")]
    public async Task<IActionResult> GetSpeakerSnippetAsync(
        string id,
        int speakerIndex,
        CancellationToken cancellationToken = default)
    {
        var recording = await _ledgerManager.GetRecordingManifestAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }

        if (recording.Speakers is null || speakerIndex < 0 || speakerIndex >= recording.Speakers.Count)
        {
            return NotFound();
        }

        var speaker = recording.Speakers[speakerIndex];
        if (speaker.SoloSnippet is null)
        {
            return NotFound();
        }

        var fullAudioPath = _vaultPathResolver.ResolvePlatformPath(recording.StagingAudioPath);
        if (!System.IO.File.Exists(fullAudioPath))
        {
            return NotFound();
        }

        var fileInfo = new FileInfo(fullAudioPath);
        var totalBytes = fileInfo.Length;
        var range = AudioSnippetStreamer.CalculateByteRange(
            totalBytes,
            recording.DurationSeconds,
            speaker.SoloSnippet.StartMs,
            speaker.SoloSnippet.EndMs);

        Response.StatusCode = StatusCodes.Status206PartialContent;
        Response.Headers["Content-Range"] = $"bytes {range.StartByte}-{range.EndByte}/{range.TotalBytes}";
        Response.Headers["Accept-Ranges"] = "bytes";
        Response.ContentType = "audio/wav";
        Response.ContentLength = range.Length;

        await _audioSnippetStreamer.StreamSnippetToAsync(
            fullAudioPath,
            range.StartByte,
            range.EndByte,
            Response.Body,
            cancellationToken);

        return new EmptyResult();
    }
}
