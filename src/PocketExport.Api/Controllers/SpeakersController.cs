using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketExport.Api.Models;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Speakers;

namespace PocketExport.Api.Controllers;

/// <summary>
/// API controller exposing endpoints for speaker voicebank enrollment and batch approval of proposed retroactive matches.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SpeakersController : ControllerBase
{
    private readonly ISpeakerVoicebankService _voicebankService;
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;
    private readonly ILogger<SpeakersController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpeakersController"/> class.
    /// </summary>
    /// <param name="voicebankService">The speaker voicebank service.</param>
    /// <param name="ledgerManager">The ledger manager instance.</param>
    /// <param name="sqliteContext">The local SQLite index context.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SpeakersController(
        ISpeakerVoicebankService voicebankService,
        ILedgerManager ledgerManager,
        SqliteIndexContext sqliteContext,
        ILogger<SpeakersController>? logger = null)
    {
        _voicebankService = voicebankService ?? throw new ArgumentNullException(nameof(voicebankService));
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        _sqliteContext = sqliteContext ?? throw new ArgumentNullException(nameof(sqliteContext));
        _logger = logger ?? NullLogger<SpeakersController>.Instance;
    }

    /// <summary>
    /// Enrolls a new speaker profile into the voicebank ledger and SQLite index.
    /// </summary>
    /// <param name="request">The speaker enrollment request payload.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The newly enrolled speaker profile if successful; otherwise, 400 BadRequest.</returns>
    [HttpPost("enroll")]
    public async Task<IActionResult> EnrollAsync(
        [FromBody] EnrollSpeakerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Speaker name is required.");
        }

        if (request.Embedding is null || request.Embedding.Length == 0)
        {
            return BadRequest("Speaker biometric embedding vector is required.");
        }

        if (request.Bounds is null)
        {
            return BadRequest("Solo snippet bounds are required.");
        }

        var profile = await _voicebankService.EnrollSpeakerAsync(
            request.Name,
            request.Organization,
            request.Embedding,
            request.RecordingId,
            request.Bounds,
            cancellationToken);

        return Ok(profile);
    }

    /// <summary>
    /// Batch approves proposed retroactive speaker matches across historical recordings.
    /// </summary>
    /// <param name="request">The batch approval request containing match record identifiers.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>An OK result with approval summary counts; otherwise, 400 BadRequest or 500 on failure.</returns>
    [HttpPost("retroactive-matches/approve")]
    public async Task<IActionResult> ApproveRetroactiveMatchesAsync(
        [FromBody] ApproveRetroactiveMatchesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MatchIds is null || request.MatchIds.Count == 0)
        {
            return BadRequest("Match IDs collection must not be empty.");
        }

        try
        {
            await _sqliteContext.InitializeDatabaseAsync(cancellationToken);
            using var connection = _sqliteContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            const string selectSql = """
                SELECT id AS Id, recording_id AS RecordingId, cluster_id AS ClusterId, proposed_speaker_id AS ProposedSpeakerId
                FROM proposed_retroactive_matches
                WHERE id IN @MatchIds;
                """;

            var matches = (await connection.QueryAsync<RetroactiveMatchRow>(
                new CommandDefinition(selectSql, new { MatchIds = request.MatchIds }, cancellationToken: cancellationToken))).ToList();

            var now = DateTimeOffset.UtcNow.ToString("O");
            const string updateStatusSql = """
                UPDATE proposed_retroactive_matches
                SET status = 'approved', reviewed_at = @ReviewedAt
                WHERE id IN @MatchIds;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                updateStatusSql,
                new { MatchIds = request.MatchIds, ReviewedAt = now },
                cancellationToken: cancellationToken));

            foreach (var match in matches)
            {
                const string updateSpeakerSql = """
                    UPDATE recording_speakers
                    SET enrolled_speaker_id = @SpeakerId, needs_disambiguation = 0
                    WHERE recording_id = @RecordingId AND cluster_id = @ClusterId;
                    """;

                await connection.ExecuteAsync(new CommandDefinition(
                    updateSpeakerSql,
                    new { SpeakerId = match.ProposedSpeakerId, RecordingId = match.RecordingId, ClusterId = match.ClusterId },
                    cancellationToken: cancellationToken));

                var manifest = await _ledgerManager.GetRecordingManifestAsync(match.RecordingId, cancellationToken);
                if (manifest?.Speakers is not null)
                {
                    var updatedSpeakers = manifest.Speakers.Select(s =>
                        s.ClusterId == match.ClusterId
                            ? s with { EnrolledSpeakerId = match.ProposedSpeakerId, NeedsDisambiguation = false }
                            : s).ToList();

                    await _ledgerManager.SaveRecordingManifestAsync(manifest with { Speakers = updatedSpeakers }, cancellationToken);
                }
            }

            return Ok(new { ApprovedCount = request.MatchIds.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve retroactive speaker matches for {Count} match IDs.", request.MatchIds.Count);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Error = "Failed to approve retroactive matches." });
        }
    }

    private sealed record RetroactiveMatchRow(long Id, string RecordingId, string ClusterId, string ProposedSpeakerId);
}
