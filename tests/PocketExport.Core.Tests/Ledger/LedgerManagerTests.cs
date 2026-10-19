using FluentAssertions;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Ledger;

/// <summary>
///     Unit and contract tests specifying atomic persistence and concurrency retry
///     semantics for the file-based ledger.
/// </summary>
public sealed class LedgerManagerTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;

    public LedgerManagerTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-ledger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
    }

    public void Dispose()
    {
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
    public async Task SaveRecordingManifestAsync_WithValidManifest_WritesAtomicallyViaTmpFileAndCreatesLedgerJson()
    {
        // Arrange
        ILedgerManager sut = new LedgerManager(_vaultPathResolver);
        var manifest = new RecordingManifest(
            Id: "rec_2026_001",
            PocketId: "pkt_abc123",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Quarterly Planning",
            DurationSeconds: 154.2,
            StagingAudioPath: "staging/rec_2026_001.m4a",
            PocketMetadataPath: "staging/rec_2026_001.json",
            RoutedTranscriptPath: "routed/transcripts/rec_2026_001.md",
            RoutedSummaryPath: "routed/summaries/rec_2026_001.md",
            Status: "completed",
            Speakers:
            [
                new RecordingSpeaker(
                    ClusterId: "cluster_0",
                    EnrolledSpeakerId: "spk_dave",
                    DisplayName: "Dave",
                    MatchConfidence: 0.94)
            ],
            Classification: new ClassificationResult(
                Domain: "Engineering",
                Customer: "Internal",
                Project: "PocketExport",
                Initiative: "V1",
                Confidence: 0.98,
                TemplateId: "tmpl_eng_sync"));

        var expectedRelativePath = ".ledger/recordings/rec_2026_001.json";
        var expectedAbsolutePath = _vaultPathResolver.ResolvePlatformPath(expectedRelativePath);

        // Act
        await sut.SaveRecordingManifestAsync(manifest);

        // Assert
        File.Exists(expectedAbsolutePath).Should().BeTrue("manifest must be saved at .ledger/recordings/{id}.json");

        var recordingsDir = Path.GetDirectoryName(expectedAbsolutePath)!;
        Directory.GetFiles(recordingsDir, "*.tmp").Should().BeEmpty("temporary write files must be atomically moved or removed");

        var loaded = await sut.GetRecordingManifestAsync("rec_2026_001");
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("rec_2026_001");
        loaded.Title.Should().Be("Quarterly Planning");
        loaded.Status.Should().Be("completed");
        loaded.Speakers.Should().HaveCount(1);
        loaded.Speakers![0].DisplayName.Should().Be("Dave");
        loaded.Classification.Should().NotBeNull();
        loaded.Classification!.Domain.Should().Be("Engineering");
    }

    [Fact]
    public async Task SaveSpeakerProfileAsync_WithValidProfile_WritesAtomicallyViaTmpFileAndCreatesLedgerJson()
    {
        // Arrange
        ILedgerManager sut = new LedgerManager(_vaultPathResolver);
        var profile = new SpeakerProfile(
            Id: "spk_dave",
            DisplayName: "Dave",
            Organization: "Pocket AI",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_2026_001",
            SampleSnippetBounds: new SoloSnippetBounds(StartMs: 500, EndMs: 3500),
            Embedding: [0.12f, -0.45f, 0.88f, 0.05f]);

        var expectedRelativePath = ".ledger/speakers/spk_dave.json";
        var expectedAbsolutePath = _vaultPathResolver.ResolvePlatformPath(expectedRelativePath);

        // Act
        await sut.SaveSpeakerProfileAsync(profile);

        // Assert
        File.Exists(expectedAbsolutePath).Should().BeTrue("speaker profile must be saved at .ledger/speakers/{id}.json");

        var speakersDir = Path.GetDirectoryName(expectedAbsolutePath)!;
        Directory.GetFiles(speakersDir, "*.tmp").Should().BeEmpty("temporary write files must be atomically moved or removed");

        var loaded = await sut.GetSpeakerProfileAsync("spk_dave");
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("spk_dave");
        loaded.DisplayName.Should().Be("Dave");
        loaded.Embedding.Should().Equal([0.12f, -0.45f, 0.88f, 0.05f]);
        loaded.SampleSnippetBounds.StartMs.Should().Be(500);
        loaded.SampleSnippetBounds.EndMs.Should().Be(3500);
    }

    [Fact]
    public async Task SaveRecordingManifestAsync_WhenFileIsTemporarilyLocked_RetriesWithBackoffAndSucceeds()
    {
        // Arrange
        ILedgerManager sut = new LedgerManager(_vaultPathResolver);
        var manifest = new RecordingManifest(
            Id: "rec_locked_retry",
            PocketId: "pkt_locked",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Locked Retry Test",
            DurationSeconds: 45.0,
            StagingAudioPath: "staging/rec_locked_retry.m4a");

        var targetRelativePath = ".ledger/recordings/rec_locked_retry.json";
        var targetAbsolutePath = _vaultPathResolver.ResolvePlatformPath(targetRelativePath);

        // Pre-create file and acquire exclusive lock simulating OneDrive sync engine lock
        await File.WriteAllTextAsync(targetAbsolutePath, "{\"locked\": true}");

        using (var lockSignal = new ManualResetEventSlim(false))
        {
            var lockTask = Task.Run(async () =>
            {
                using var stream = new FileStream(
                    targetAbsolutePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

                lockSignal.Set();
                // Hold lock for 400ms to simulate transient sync lock, then release
                await Task.Delay(400);
            });

            lockSignal.Wait(TimeSpan.FromSeconds(2));

            // Act - should retry with linear backoff (up to 5s) and succeed once lock is released
            await sut.SaveRecordingManifestAsync(manifest);
            await lockTask;
        }

        // Assert
        var loaded = await sut.GetRecordingManifestAsync("rec_locked_retry");
        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("Locked Retry Test");
    }

    [Fact]
    public async Task SaveRecordingManifestAsync_WhenFileIsLockedExceedingTimeout_ThrowsIOExceptionAfterRetries()
    {
        // Arrange
        ILedgerManager sut = new LedgerManager(_vaultPathResolver);
        var manifest = new RecordingManifest(
            Id: "rec_permanent_lock",
            PocketId: "pkt_perm_lock",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Permanent Lock Test",
            DurationSeconds: 10.0,
            StagingAudioPath: "staging/rec_permanent_lock.m4a");

        var targetRelativePath = ".ledger/recordings/rec_permanent_lock.json";
        var targetAbsolutePath = _vaultPathResolver.ResolvePlatformPath(targetRelativePath);

        await File.WriteAllTextAsync(targetAbsolutePath, "{\"locked\": true}");

        using var stream = new FileStream(
            targetAbsolutePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        // Act & Assert
        // After exhausting 5 seconds of retries against the locked destination, it throws IOException
        var act = () => sut.SaveRecordingManifestAsync(manifest);
        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task GetAllRecordingManifestsAsync_WhenMultipleManifestsExist_ReturnsAllManifests()
    {
        // Arrange
        ILedgerManager sut = new LedgerManager(_vaultPathResolver);
        var manifest1 = new RecordingManifest(
            Id: "rec_all_1",
            PocketId: "pkt_all_1",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Manifest 1",
            DurationSeconds: 10.0,
            StagingAudioPath: "staging/rec_all_1.m4a");
        var manifest2 = new RecordingManifest(
            Id: "rec_all_2",
            PocketId: "pkt_all_2",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Manifest 2",
            DurationSeconds: 20.0,
            StagingAudioPath: "staging/rec_all_2.m4a");

        await sut.SaveRecordingManifestAsync(manifest1);
        await sut.SaveRecordingManifestAsync(manifest2);

        // Act
        var all = await sut.GetAllRecordingManifestsAsync();

        // Assert
        all.Should().HaveCount(2);
        all.Select(m => m.Id).Should().BeEquivalentTo(["rec_all_1", "rec_all_2"]);
    }
}
