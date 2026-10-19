using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Ledger;

/// <summary>
///     Integration tests specifying SQLite projection hydration and upsert behaviors
///     from filesystem ledger manifests.
/// </summary>
public sealed class LedgerProjectionServiceTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly string _dbPath;
    private readonly IVaultPathResolver _vaultPathResolver;

    public LedgerProjectionServiceTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-projection-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
        _dbPath = Path.Combine(_testVaultDir, "local-index.db");
    }

    public void Dispose()
    {
        // Clear SQLite pool so database file can be deleted
        SqliteConnection.ClearAllPools();

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
    public async Task SqliteIndexContext_InitializeDatabaseAsync_CreatesExpectedSchemaTables()
    {
        // Arrange
        using var context = new SqliteIndexContext(_dbPath);

        // Act
        await context.InitializeDatabaseAsync();

        // Assert
        using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var tableNames = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';")).ToList();

        tableNames.Should().Contain([
            "recordings",
            "speakers",
            "recording_speakers",
            "proposed_retroactive_matches"
        ]);
    }

    [Fact]
    public async Task HydrateAllAsync_WhenDiskContainsManifests_PopulatesSqliteIndexFromScratch()
    {
        // Arrange
        ILedgerManager ledgerManager = new LedgerManager(_vaultPathResolver);

        var manifest1 = new RecordingManifest(
            Id: "rec_hydrate_1",
            PocketId: "pkt_hyd_1",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "First Hydration Meeting",
            DurationSeconds: 120.0,
            StagingAudioPath: "staging/rec_hydrate_1.m4a",
            RoutedTranscriptPath: "routed/transcripts/rec_hydrate_1.md",
            Status: "completed",
            Speakers:
            [
                new RecordingSpeaker(
                    ClusterId: "cluster_0",
                    EnrolledSpeakerId: "spk_alice",
                    DisplayName: "Alice",
                    MatchConfidence: 0.95),
                new RecordingSpeaker(
                    ClusterId: "cluster_1",
                    DisplayName: "Bob (Unenrolled)",
                    NeedsDisambiguation: true)
            ],
            Classification: new ClassificationResult(
                Domain: "Engineering",
                Customer: "Contoso",
                Project: "Apollo",
                Initiative: "Sprint 42",
                Confidence: 0.92,
                TemplateId: "tmpl_eng"));

        var manifest2 = new RecordingManifest(
            Id: "rec_hydrate_2",
            PocketId: "pkt_hyd_2",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Second Hydration Standup",
            DurationSeconds: 60.0,
            StagingAudioPath: "staging/rec_hydrate_2.m4a",
            Status: "pending");

        var speaker = new SpeakerProfile(
            Id: "spk_alice",
            DisplayName: "Alice",
            Organization: "Contoso",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_hydrate_1",
            SampleSnippetBounds: new SoloSnippetBounds(100, 3000),
            Embedding: [0.1f, 0.2f, 0.3f]);

        await ledgerManager.SaveRecordingManifestAsync(manifest1);
        await ledgerManager.SaveRecordingManifestAsync(manifest2);
        await ledgerManager.SaveSpeakerProfileAsync(speaker);

        using var context = new SqliteIndexContext(_dbPath);
        await context.InitializeDatabaseAsync();

        var sut = new LedgerProjectionService(_vaultPathResolver, ledgerManager, context);

        // Act
        await sut.HydrateAllAsync();

        // Assert
        using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var recordingCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recordings;");
        recordingCount.Should().Be(2);

        var speakerCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM speakers;");
        speakerCount.Should().Be(1);

        var recordingSpeakerCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recording_speakers;");
        recordingSpeakerCount.Should().Be(2);

        var firstRec = await connection.QuerySingleAsync<dynamic>(
            "SELECT id, title, status, domain, customer, project FROM recordings WHERE id = @Id;",
            new { Id = "rec_hydrate_1" });

        ((string)firstRec.id).Should().Be("rec_hydrate_1");
        ((string)firstRec.title).Should().Be("First Hydration Meeting");
        ((string)firstRec.status).Should().Be("completed");
        ((string)firstRec.domain).Should().Be("Engineering");
        ((string)firstRec.customer).Should().Be("Contoso");
        ((string)firstRec.project).Should().Be("Apollo");
    }

    [Fact]
    public async Task ProjectRecordingAsync_WhenManifestIsModified_UpsertsSqliteRecordWithoutDuplication()
    {
        // Arrange
        ILedgerManager ledgerManager = new LedgerManager(_vaultPathResolver);
        using var context = new SqliteIndexContext(_dbPath);
        await context.InitializeDatabaseAsync();

        var sut = new LedgerProjectionService(_vaultPathResolver, ledgerManager, context);

        var initialManifest = new RecordingManifest(
            Id: "rec_upsert_1",
            PocketId: "pkt_upsert_1",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Initial Title",
            DurationSeconds: 80.0,
            StagingAudioPath: "staging/rec_upsert_1.m4a",
            Status: "pending");

        await ledgerManager.SaveRecordingManifestAsync(initialManifest);
        await sut.ProjectRecordingAsync(initialManifest);

        using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var initialCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recordings;");
        initialCount.Should().Be(1);

        var initialTitle = await connection.ExecuteScalarAsync<string>(
            "SELECT title FROM recordings WHERE id = @Id;",
            new { Id = "rec_upsert_1" });
        initialTitle.Should().Be("Initial Title");

        // Act - Update the manifest with new status, title, and transcript path
        var updatedManifest = initialManifest with
        {
            Title = "Updated Title After Processing",
            Status = "completed",
            RoutedTranscriptPath = "routed/transcripts/rec_upsert_1.md"
        };

        await ledgerManager.SaveRecordingManifestAsync(updatedManifest);
        await sut.ProjectRecordingAsync(updatedManifest);

        // Assert - verify upsert: count must still be 1, but values updated
        var postUpdateCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recordings;");
        postUpdateCount.Should().Be(1, "upsert must update existing row rather than inserting duplicate");

        var updatedRow = await connection.QuerySingleAsync<dynamic>(
            "SELECT title, status, routed_transcript_path FROM recordings WHERE id = @Id;",
            new { Id = "rec_upsert_1" });

        ((string)updatedRow.title).Should().Be("Updated Title After Processing");
        ((string)updatedRow.status).Should().Be("completed");
        ((string)updatedRow.routed_transcript_path).Should().Be("routed/transcripts/rec_upsert_1.md");
    }

    [Fact]
    public async Task HydrateAllAsync_WhenRunIdempotentlyMultipleTimes_MaintainsExactRowCounts()
    {
        // Arrange
        ILedgerManager ledgerManager = new LedgerManager(_vaultPathResolver);
        using var context = new SqliteIndexContext(_dbPath);
        await context.InitializeDatabaseAsync();

        var sut = new LedgerProjectionService(_vaultPathResolver, ledgerManager, context);

        var manifest = new RecordingManifest(
            Id: "rec_idempotent",
            PocketId: "pkt_idem",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Idempotency Test",
            DurationSeconds: 30.0,
            StagingAudioPath: "staging/rec_idempotent.m4a",
            Status: "completed");

        var speaker = new SpeakerProfile(
            Id: "spk_idempotent",
            DisplayName: "Charlie",
            Organization: "Contoso",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_idempotent",
            SampleSnippetBounds: new SoloSnippetBounds(0, 1000),
            Embedding: [0.5f, 0.5f]);

        await ledgerManager.SaveRecordingManifestAsync(manifest);
        await ledgerManager.SaveSpeakerProfileAsync(speaker);

        // Act - Run HydrateAllAsync twice
        await sut.HydrateAllAsync();
        await sut.HydrateAllAsync();

        // Assert - row counts must not be duplicated
        using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var recCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recordings;");
        var spkCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM speakers;");

        recCount.Should().Be(1);
        spkCount.Should().Be(1);
    }
}
