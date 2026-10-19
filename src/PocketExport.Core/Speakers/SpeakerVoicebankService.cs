namespace PocketExport.Core.Speakers;

using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;

/// <summary>
/// Implements biometric voicebank management, speaker matching with threshold classification,
/// disambiguation tagging, and retroactive historical match scanning.
/// </summary>
public sealed class SpeakerVoicebankService : ISpeakerVoicebankService
{
    private const double MatchThreshold = 0.75;
    private const double DisambiguationThreshold = 0.70;
    private const double CloudHintBoost = 0.05;

    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpeakerVoicebankService"/> class.
    /// </summary>
    /// <param name="ledgerManager">The ledger storage manager.</param>
    /// <param name="sqliteContext">The local SQLite index context.</param>
    public SpeakerVoicebankService(ILedgerManager ledgerManager, SqliteIndexContext sqliteContext)
    {
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        _sqliteContext = sqliteContext ?? throw new ArgumentNullException(nameof(sqliteContext));
    }

    /// <inheritdoc />
    public async Task<SpeakerMatchResult> MatchSpeakerAsync(
        float[] embedding,
        IReadOnlyList<string>? cloudSpeakerHints = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding vector must not be empty.", nameof(embedding));
        }

        var candidates = await LoadEnrolledSpeakersAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return new SpeakerMatchResult(
                SpeakerId: null,
                DisplayName: "SPEAKER_00",
                Confidence: 0.0,
                NeedsDisambiguation: false);
        }

        EnrolledCandidate? bestCandidate = null;
        var bestScore = double.MinValue;

        foreach (var candidate in candidates)
        {
            if (candidate.Embedding.Length != embedding.Length)
            {
                continue;
            }

            var similarity = VectorMath.CosineSimilarity(embedding, candidate.Embedding);
            if (HasCloudHintMatch(candidate.DisplayName, cloudSpeakerHints))
            {
                similarity = Math.Min(1.0, similarity + CloudHintBoost);
            }

            if (similarity > bestScore)
            {
                bestScore = similarity;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is null || bestScore < DisambiguationThreshold)
        {
            return new SpeakerMatchResult(
                SpeakerId: null,
                DisplayName: "SPEAKER_00",
                Confidence: bestScore < 0.0 ? 0.0 : bestScore,
                NeedsDisambiguation: false);
        }

        if (bestScore >= MatchThreshold)
        {
            return new SpeakerMatchResult(
                SpeakerId: bestCandidate.Id,
                DisplayName: bestCandidate.DisplayName,
                Confidence: bestScore,
                NeedsDisambiguation: false);
        }

        return new SpeakerMatchResult(
            SpeakerId: bestCandidate.Id,
            DisplayName: bestCandidate.DisplayName,
            Confidence: bestScore,
            NeedsDisambiguation: true);
    }

    /// <inheritdoc />
    public async Task<SpeakerProfile> EnrollSpeakerAsync(
        string name,
        string organization,
        float[] embedding,
        string recordingId,
        SoloSnippetBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentNullException.ThrowIfNull(bounds);

        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding vector must not be empty.", nameof(embedding));
        }

        var profileId = $"spk_{Guid.NewGuid():N}";
        var profile = new SpeakerProfile(
            Id: profileId,
            DisplayName: name,
            Organization: organization,
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: recordingId,
            SampleSnippetBounds: bounds,
            Embedding: embedding);

        // 1. Persist to ledger file
        await _ledgerManager.SaveSpeakerProfileAsync(profile, cancellationToken);

        // 2. Persist to SQLite index
        using var connection = _sqliteContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string insertSql = """
            INSERT INTO speakers (id, display_name, organization, enrolled_at, source_recording_id, sample_start_ms, sample_end_ms, embedding)
            VALUES (@Id, @DisplayName, @Organization, @EnrolledAt, @SourceRecordingId, @StartMs, @EndMs, @Embedding)
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                organization = excluded.organization,
                enrolled_at = excluded.enrolled_at,
                source_recording_id = excluded.source_recording_id,
                sample_start_ms = excluded.sample_start_ms,
                sample_end_ms = excluded.sample_end_ms,
                embedding = excluded.embedding;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                new
                {
                    Id = profile.Id,
                    DisplayName = profile.DisplayName,
                    Organization = profile.Organization,
                    EnrolledAt = profile.EnrolledAt.ToString("O"),
                    SourceRecordingId = profile.SourceRecordingId,
                    StartMs = bounds.StartMs,
                    EndMs = bounds.EndMs,
                    Embedding = JsonSerializer.Serialize(profile.Embedding)
                },
                cancellationToken: cancellationToken));

        // 3. Scan historical recordings for unresolved speakers and propose retroactive matches
        await ScanAndProposeRetroactiveMatchesAsync(connection, profile, cancellationToken);

        return profile;
    }

    private async Task<List<EnrolledCandidate>> LoadEnrolledSpeakersAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<EnrolledCandidate>();

        try
        {
            using var connection = _sqliteContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            const string sql = "SELECT id AS Id, display_name AS DisplayName, embedding AS Embedding FROM speakers;";
            var rows = (await connection.QueryAsync<SpeakerDbRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Embedding))
                {
                    continue;
                }

                try
                {
                    var vec = JsonSerializer.Deserialize<float[]>(row.Embedding);
                    if (vec is not null && vec.Length > 0)
                    {
                        candidates.Add(new EnrolledCandidate(row.Id, row.DisplayName, vec));
                    }
                }
                catch (JsonException)
                {
                    // Skip unparseable embedding
                }
            }
        }
        catch (Exception)
        {
            // Fall back to ledger if SQLite table query fails
        }

        if (candidates.Count == 0)
        {
            var profiles = await _ledgerManager.GetAllSpeakerProfilesAsync(cancellationToken);
            foreach (var profile in profiles)
            {
                if (profile.Embedding is { Count: > 0 })
                {
                    var vec = profile.Embedding is float[] arr ? arr : profile.Embedding.ToArray();
                    candidates.Add(new EnrolledCandidate(profile.Id, profile.DisplayName, vec));
                }
            }
        }

        return candidates;
    }

    private static async Task ScanAndProposeRetroactiveMatchesAsync(
        SqliteConnection connection,
        SpeakerProfile profile,
        CancellationToken cancellationToken)
    {
        const string querySql = """
            SELECT recording_id AS RecordingId, cluster_id AS ClusterId, embedding_vector AS EmbeddingVector
            FROM recording_speakers
            WHERE (enrolled_speaker_id IS NULL OR enrolled_speaker_id = '')
              AND embedding_vector IS NOT NULL;
            """;

        var unresolvedRows = (await connection.QueryAsync<UnresolvedSpeakerRow>(
            new CommandDefinition(querySql, cancellationToken: cancellationToken))).ToList();

        if (unresolvedRows.Count == 0)
        {
            return;
        }

        const string insertProposedSql = """
            INSERT INTO proposed_retroactive_matches (recording_id, cluster_id, proposed_speaker_id, confidence, status, created_at)
            VALUES (@RecordingId, @ClusterId, @ProposedSpeakerId, @Confidence, 'proposed', @CreatedAt);
            """;

        var now = DateTimeOffset.UtcNow.ToString("O");

        foreach (var row in unresolvedRows)
        {
            if (string.IsNullOrWhiteSpace(row.EmbeddingVector))
            {
                continue;
            }

            float[]? unresolvedVec = null;
            try
            {
                unresolvedVec = JsonSerializer.Deserialize<float[]>(row.EmbeddingVector);
            }
            catch (JsonException)
            {
                continue;
            }

            if (unresolvedVec is null || unresolvedVec.Length != profile.Embedding.Count)
            {
                continue;
            }

            var similarity = VectorMath.CosineSimilarity(profile.Embedding, unresolvedVec);
            if (similarity >= MatchThreshold)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertProposedSql,
                        new
                        {
                            RecordingId = row.RecordingId,
                            ClusterId = row.ClusterId,
                            ProposedSpeakerId = profile.Id,
                            Confidence = similarity,
                            CreatedAt = now
                        },
                        cancellationToken: cancellationToken));
            }
        }
    }

    private static bool HasCloudHintMatch(string displayName, IReadOnlyList<string>? hints)
    {
        if (hints is null || hints.Count == 0 || string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var trimmedName = displayName.Trim();

        foreach (var hint in hints)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            var trimmedHint = hint.Trim();

            if (string.Equals(trimmedName, trimmedHint, StringComparison.OrdinalIgnoreCase) ||
                trimmedName.Contains(trimmedHint, StringComparison.OrdinalIgnoreCase) ||
                trimmedHint.Contains(trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record EnrolledCandidate(string Id, string DisplayName, float[] Embedding);

    private sealed record SpeakerDbRow(string Id, string DisplayName, string Embedding);

    private sealed record UnresolvedSpeakerRow(string RecordingId, string ClusterId, string? EmbeddingVector);
}
