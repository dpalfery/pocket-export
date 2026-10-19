using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Speakers;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Speakers;

/// <summary>
/// Unit and integration test contract specifying vector mathematics, speaker profile enrollment,
/// threshold matching, disambiguation, and retroactive historical match scanning.
/// </summary>
public sealed class SpeakerVoicebankServiceTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly string _dbPath;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;

    public SpeakerVoicebankServiceTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-voicebank-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
        _ledgerManager = new LedgerManager(_vaultPathResolver);
        _dbPath = Path.Combine(_testVaultDir, "local-index.db");
        _sqliteContext = new SqliteIndexContext(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _sqliteContext.Dispose();

        if (Directory.Exists(_testVaultDir))
        {
            try
            {
                Directory.Delete(_testVaultDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public void CosineSimilarity_WhenVectorsAreIdentical_ReturnsOne()
    {
        // Arrange
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 1f, 2f, 3f };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(1.0, 1e-4);
    }

    [Fact]
    public void CosineSimilarity_WhenVectorsAreOrthogonal_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { 0f, 1f, 0f };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(0.0, 1e-4);
    }

    [Fact]
    public void CosineSimilarity_WhenVectorsAreOpposite_ReturnsNegativeOne()
    {
        // Arrange
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { -1f, 0f, 0f };

        // Act
        var similarity = VectorMath.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(-1.0, 1e-4);
    }

    [Fact]
    public void CosineSimilarity_WhenVectorsHaveDifferentLengths_ThrowsArgumentException()
    {
        // Arrange
        var a = new float[] { 1f, 0f };
        var b = new float[] { 1f, 0f, 0f };

        // Act
        var act = () => VectorMath.CosineSimilarity(a, b);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task MatchSpeakerAsync_WhenMatchScoreAtOrAboveThreshold_MatchesEnrolledSpeaker()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();
        var aliceEmbedding = CreateNormalizedVector(512, 0);
        var bounds = new SoloSnippetBounds(1000, 5000);
        var aliceProfile = new SpeakerProfile(
            Id: "spk_alice",
            DisplayName: "Alice Smith",
            Organization: "Acme Corp",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_001",
            SampleSnippetBounds: bounds,
            Embedding: aliceEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(aliceProfile);

        using (var connection = _sqliteContext.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO speakers (id, display_name, organization, enrolled_at, source_recording_id, sample_start_ms, sample_end_ms, embedding)
                VALUES (@Id, @DisplayName, @Organization, @EnrolledAt, @SourceRecordingId, @StartMs, @EndMs, @Embedding)
                """,
                new
                {
                    Id = aliceProfile.Id,
                    DisplayName = aliceProfile.DisplayName,
                    Organization = aliceProfile.Organization,
                    EnrolledAt = aliceProfile.EnrolledAt.ToString("O"),
                    SourceRecordingId = aliceProfile.SourceRecordingId,
                    StartMs = bounds.StartMs,
                    EndMs = bounds.EndMs,
                    Embedding = JsonSerializer.Serialize(aliceEmbedding)
                });
        }

        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);
        var testEmbedding = CreateVectorWithSimilarityToUnit(512, 0.85);

        // Act
        var result = await sut.MatchSpeakerAsync(testEmbedding, new List<string>());

        // Assert
        result.SpeakerId.Should().Be("spk_alice");
        result.DisplayName.Should().Be("Alice Smith");
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.75);
        result.NeedsDisambiguation.Should().BeFalse();
    }

    [Fact]
    public async Task MatchSpeakerAsync_WhenMatchScoreInDisambiguationRange_FlagsNeedsDisambiguation()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();
        var bobEmbedding = CreateNormalizedVector(512, 0);
        var bounds = new SoloSnippetBounds(1000, 5000);
        var bobProfile = new SpeakerProfile(
            Id: "spk_bob",
            DisplayName: "Bob Jones",
            Organization: "Beta LLC",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_002",
            SampleSnippetBounds: bounds,
            Embedding: bobEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(bobProfile);

        using (var connection = _sqliteContext.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO speakers (id, display_name, organization, enrolled_at, source_recording_id, sample_start_ms, sample_end_ms, embedding)
                VALUES (@Id, @DisplayName, @Organization, @EnrolledAt, @SourceRecordingId, @StartMs, @EndMs, @Embedding)
                """,
                new
                {
                    Id = bobProfile.Id,
                    DisplayName = bobProfile.DisplayName,
                    Organization = bobProfile.Organization,
                    EnrolledAt = bobProfile.EnrolledAt.ToString("O"),
                    SourceRecordingId = bobProfile.SourceRecordingId,
                    StartMs = bounds.StartMs,
                    EndMs = bounds.EndMs,
                    Embedding = JsonSerializer.Serialize(bobEmbedding)
                });
        }

        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);
        var testEmbedding = CreateVectorWithSimilarityToUnit(512, 0.72);

        // Act
        var result = await sut.MatchSpeakerAsync(testEmbedding, new List<string>());

        // Assert
        result.SpeakerId.Should().Be("spk_bob");
        result.Confidence.Should().BeInRange(0.70, 0.7499);
        result.NeedsDisambiguation.Should().BeTrue();
    }

    [Fact]
    public async Task MatchSpeakerAsync_WhenMatchScoreBelowThreshold_MarksAsUnidentified()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();
        var speakerEmbedding = CreateNormalizedVector(512, 0);
        var bounds = new SoloSnippetBounds(1000, 5000);
        var profile = new SpeakerProfile(
            Id: "spk_charlie",
            DisplayName: "Charlie Davis",
            Organization: "Gamma Corp",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_003",
            SampleSnippetBounds: bounds,
            Embedding: speakerEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(profile);

        using (var connection = _sqliteContext.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO speakers (id, display_name, organization, enrolled_at, source_recording_id, sample_start_ms, sample_end_ms, embedding)
                VALUES (@Id, @DisplayName, @Organization, @EnrolledAt, @SourceRecordingId, @StartMs, @EndMs, @Embedding)
                """,
                new
                {
                    Id = profile.Id,
                    DisplayName = profile.DisplayName,
                    Organization = profile.Organization,
                    EnrolledAt = profile.EnrolledAt.ToString("O"),
                    SourceRecordingId = profile.SourceRecordingId,
                    StartMs = bounds.StartMs,
                    EndMs = bounds.EndMs,
                    Embedding = JsonSerializer.Serialize(speakerEmbedding)
                });
        }

        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);
        var testEmbedding = CreateVectorWithSimilarityToUnit(512, 0.50);

        // Act
        var result = await sut.MatchSpeakerAsync(testEmbedding, new List<string>());

        // Assert
        result.SpeakerId.Should().BeNull();
        result.DisplayName.Should().StartWith("SPEAKER_");
        result.Confidence.Should().BeLessThan(0.70);
        result.NeedsDisambiguation.Should().BeFalse();
    }

    [Fact]
    public async Task MatchSpeakerAsync_WhenCloudSpeakerHintsContainEnrolledSpeakerName_BoostsCandidateRanking()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();

        var aliceEmbedding = CreateNormalizedVector(512, 0);
        var bounds = new SoloSnippetBounds(1000, 5000);
        var aliceProfile = new SpeakerProfile(
            Id: "spk_alice",
            DisplayName: "Alice Walker",
            Organization: "Acme Corp",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_001",
            SampleSnippetBounds: bounds,
            Embedding: aliceEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(aliceProfile);

        var bobEmbedding = CreateNormalizedVector(512, 2);
        var bobProfile = new SpeakerProfile(
            Id: "spk_bob",
            DisplayName: "Bob Vance",
            Organization: "Vance Refrigeration",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_002",
            SampleSnippetBounds: bounds,
            Embedding: bobEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(bobProfile);

        using (var connection = _sqliteContext.CreateConnection())
        {
            await connection.OpenAsync();
            foreach (var p in new[] { aliceProfile, bobProfile })
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO speakers (id, display_name, organization, enrolled_at, source_recording_id, sample_start_ms, sample_end_ms, embedding)
                    VALUES (@Id, @DisplayName, @Organization, @EnrolledAt, @SourceRecordingId, @StartMs, @EndMs, @Embedding)
                    """,
                    new
                    {
                        Id = p.Id,
                        DisplayName = p.DisplayName,
                        Organization = p.Organization,
                        EnrolledAt = p.EnrolledAt.ToString("O"),
                        SourceRecordingId = p.SourceRecordingId,
                        StartMs = bounds.StartMs,
                        EndMs = bounds.EndMs,
                        Embedding = JsonSerializer.Serialize(p.Embedding)
                    });
            }
        }

        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);

        // Vector with raw dot product 0.71 with Alice and 0.73 with Bob
        var testEmbedding = new float[512];
        testEmbedding[0] = 0.71f;
        testEmbedding[2] = 0.73f;
        var norm = (float)Math.Sqrt((0.71 * 0.71) + (0.73 * 0.73));
        testEmbedding[0] /= norm;
        testEmbedding[2] /= norm;

        var hints = new List<string> { "Alice Walker" };

        // Act
        var result = await sut.MatchSpeakerAsync(testEmbedding, hints);

        // Assert
        result.SpeakerId.Should().Be("spk_alice");
        result.DisplayName.Should().Be("Alice Walker");
    }

    [Fact]
    public async Task EnrollSpeakerAsync_WhenCalled_PersistsProfileToLedgerAndSqliteTable()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();
        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);

        var embedding = CreateNormalizedVector(512, 1);
        var bounds = new SoloSnippetBounds(2000, 7000);

        // Act
        var profile = await sut.EnrollSpeakerAsync(
            name: "Dana Scully",
            organization: "FBI",
            embedding: embedding,
            recordingId: "rec_2026_042",
            bounds: bounds);

        // Assert
        profile.Should().NotBeNull();
        profile.Id.Should().NotBeNullOrWhiteSpace();
        profile.DisplayName.Should().Be("Dana Scully");
        profile.Organization.Should().Be("FBI");
        profile.SourceRecordingId.Should().Be("rec_2026_042");
        profile.SampleSnippetBounds.Should().Be(bounds);
        profile.Embedding.Should().Equal(embedding);

        // Assert ledger persistence
        var ledgerProfile = await _ledgerManager.GetSpeakerProfileAsync(profile.Id);
        ledgerProfile.Should().NotBeNull();
        ledgerProfile!.DisplayName.Should().Be("Dana Scully");
        ledgerProfile.Organization.Should().Be("FBI");
        var manifestFile = Path.Combine(_testVaultDir, ".ledger", "speakers", $"{profile.Id}.json");
        File.Exists(manifestFile).Should().BeTrue();

        // Assert SQLite persistence
        using var connection = _sqliteContext.CreateConnection();
        await connection.OpenAsync();
        var dbRow = await connection.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT id, display_name, organization, source_recording_id, sample_start_ms, sample_end_ms FROM speakers WHERE id = @Id",
            new { profile.Id });
        ((object?)dbRow).Should().NotBeNull();
        Assert.Equal("Dana Scully", (string)dbRow.display_name);
        Assert.Equal("FBI", (string)dbRow.organization);
        Assert.Equal("rec_2026_042", (string)dbRow.source_recording_id);
        Assert.Equal(2000L, (long)dbRow.sample_start_ms);
        Assert.Equal(7000L, (long)dbRow.sample_end_ms);
    }

    [Fact]
    public async Task EnrollSpeakerAsync_WhenHistoricalRecordingsHaveUnresolvedSpeakers_ScansAndInsertsProposedRetroactiveMatches()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();

        var targetEmbedding = CreateNormalizedVector(512, 0);

        using (var connection = _sqliteContext.CreateConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO recordings (id, pocket_id, created_at, title, duration_seconds, staging_audio_path, status)
                VALUES ('rec_hist_001', 'pkt_hist_001', '2026-01-01T00:00:00Z', 'Historical Call', 120.0, 'staging/hist.m4a', 'completed')
                """);

            var unresolvedEmbedding = CreateVectorWithSimilarityToUnit(512, 0.85);

            await connection.ExecuteAsync(
                """
                INSERT INTO recording_speakers (recording_id, cluster_id, enrolled_speaker_id, display_name, match_confidence, needs_disambiguation, sample_start_ms, sample_end_ms, embedding_vector)
                VALUES ('rec_hist_001', 'cluster_1', NULL, 'SPEAKER_01', 0.0, 0, 500, 3500, @EmbeddingJson)
                """,
                new { EmbeddingJson = JsonSerializer.Serialize(unresolvedEmbedding) });
        }

        ISpeakerVoicebankService sut = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);
        var bounds = new SoloSnippetBounds(1000, 4000);

        // Act
        var profile = await sut.EnrollSpeakerAsync(
            name: "Fox Mulder",
            organization: "FBI",
            embedding: targetEmbedding,
            recordingId: "rec_2026_099",
            bounds: bounds);

        // Assert
        using var verifyConn = _sqliteContext.CreateConnection();
        await verifyConn.OpenAsync();
        var matches = (await verifyConn.QueryAsync<dynamic>(
            "SELECT recording_id, cluster_id, proposed_speaker_id, confidence, status FROM proposed_retroactive_matches WHERE proposed_speaker_id = @SpeakerId",
            new { SpeakerId = profile.Id })).ToList();

        matches.Should().HaveCount(1);
        Assert.Equal("rec_hist_001", (string)matches[0].recording_id);
        Assert.Equal("cluster_1", (string)matches[0].cluster_id);
        Assert.Equal(profile.Id, (string)matches[0].proposed_speaker_id);
        Assert.Equal("proposed", (string)matches[0].status);
        ((double)matches[0].confidence).Should().BeGreaterThanOrEqualTo(0.75);
    }

    private static float[] CreateNormalizedVector(int dimensions, int nonZeroIndex = 0)
    {
        var vector = new float[dimensions];
        vector[nonZeroIndex] = 1.0f;
        return vector;
    }

    private static float[] CreateVectorWithSimilarityToUnit(int dimensions, double targetCosineSimilarity)
    {
        var vector = new float[dimensions];
        var cos = (float)targetCosineSimilarity;
        var sin = (float)Math.Sqrt(Math.Max(0.0, 1.0 - (targetCosineSimilarity * targetCosineSimilarity)));
        vector[0] = cos;
        vector[1] = sin;
        return vector;
    }
}
