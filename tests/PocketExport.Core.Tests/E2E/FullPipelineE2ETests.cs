using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Metadata;
using PocketExport.Core.Ml;
using PocketExport.Core.Models;
using PocketExport.Core.Orchestration;
using PocketExport.Core.Pocket;
using PocketExport.Core.Routing;
using PocketExport.Core.Speakers;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using PocketExport.Core.Taxonomy;
using Xunit;

namespace PocketExport.Core.Tests.E2E;

/// <summary>
/// Glue orchestration fixture coordinating all pipeline stages: Pocket API sync -> staging ->
/// transcription &amp; diarization -> biometric voicebank matching -> taxonomy classification ->
/// audio container tagging -> folder routing -> template summarization -> SQLite projection.
/// </summary>
public sealed class FullPipelineGlueFixture
{
    private readonly IFullPipelineOrchestrator _orchestrator;

    public FullPipelineGlueFixture(
        IVaultPathResolver vaultPathResolver,
        ILedgerManager ledgerManager,
        SqliteIndexContext sqliteContext,
        IPocketApiClient pocketApiClient,
        IAudioWorkerClient audioWorkerClient,
        ISpeakerVoicebankService speakerVoicebankService,
        ITaxonomyClassifier taxonomyClassifier,
        IAudioContainerTagger audioContainerTagger,
        IVaultFolderRouter vaultFolderRouter,
        ITemplateSummarizer templateSummarizer,
        LedgerProjectionService ledgerProjectionService)
    {
        _orchestrator = new FullPipelineOrchestrator(
            vaultPathResolver,
            ledgerManager,
            pocketApiClient,
            audioWorkerClient,
            speakerVoicebankService,
            taxonomyClassifier,
            audioContainerTagger,
            vaultFolderRouter,
            templateSummarizer,
            ledgerProjectionService);
    }

    /// <summary>
    /// Executes the full recording processing pipeline end-to-end.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A strongly typed pipeline execution result containing platform paths and the final manifest.</returns>
    public async Task<FullPipelineExecutionResult> ExecutePipelineAsync(CancellationToken cancellationToken = default)
    {
        var results = await _orchestrator.ExecuteFullPipelineAsync(cancellationToken);
        return results.First();
    }
}

/// <summary>
/// End-to-end integration test contract specifying the complete recording processing pipeline:
/// synthetic Pocket API response -&gt; staging raw audio &amp; metadata download -&gt; transcription &amp; diarization -&gt;
/// biometric voicebank matching -&gt; taxonomy classification -&gt; audio container tagging via TagLibSharp -&gt;
/// folder routing to routed/ -&gt; template-based Markdown summarization -&gt; SQLite index verification.
/// </summary>
public sealed class FullPipelineE2ETests : IDisposable
{
    static FullPipelineE2ETests()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _testVaultDir;
    private readonly string _dbPath;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;
    private readonly LedgerProjectionService _projectionService;
    private readonly IAudioContainerTagger _tagger;
    private readonly IVaultFolderRouter _folderRouter;

    public FullPipelineE2ETests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);

        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();

        _ledgerManager = new LedgerManager(_vaultPathResolver);
        _dbPath = Path.Combine(_testVaultDir, "local-index.db");
        _sqliteContext = new SqliteIndexContext(_dbPath);
        _projectionService = new LedgerProjectionService(_vaultPathResolver, _ledgerManager, _sqliteContext);
        _tagger = new AudioContainerTagger();
        _folderRouter = new VaultFolderRouter(_vaultPathResolver);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _projectionService.Dispose();
        _sqliteContext.Dispose();

        if (Directory.Exists(_testVaultDir))
        {
            try
            {
                Directory.Delete(_testVaultDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of test temp directory
            }
        }
    }

    [Fact]
    public async Task ExecuteFullPipeline_WithSyntheticPocketRecording_CompletesAllStagesAndUpdatesSqliteIndex()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();

        // 1. Seed taxonomy configuration in vault
        var taxonomyYaml = """
            domains:
              - name: Engineering
                customers:
                  - AcmeCorp
                projects:
                  - Platform
                initiatives:
                  - CloudSync
            """;
        var taxonomyPlatformPath = _vaultPathResolver.ResolvePlatformPath(".ledger/taxonomy.yaml");
        await File.WriteAllTextAsync(taxonomyPlatformPath, taxonomyYaml);

        // 2. Seed executive summary template in vault
        var templateDir = _vaultPathResolver.ResolvePlatformPath(".ledger/templates");
        Directory.CreateDirectory(templateDir);
        var templateContent = """
            # Executive Summary: {{Title}}
            **Domain:** {{Domain}} | **Customer:** {{Customer}} | **Project:** {{Project}}
            **Speakers:** {{Speakers}}

            ## Decisions
            ## Next Steps
            """;
        await File.WriteAllTextAsync(Path.Combine(templateDir, "executive-summary.md"), templateContent);

        // 3. Seed enrolled speaker in voicebank (Alice)
        var aliceEmbedding = CreateNormalizedEmbedding(512, seed: 10);
        var aliceProfile = new SpeakerProfile(
            Id: "spk_alice",
            DisplayName: "Alice Smith",
            Organization: "Engineering",
            EnrolledAt: DateTimeOffset.UtcNow.AddDays(-30),
            SourceRecordingId: "rec_seed_001",
            SampleSnippetBounds: new SoloSnippetBounds(1000, 4000),
            Embedding: aliceEmbedding);
        await _ledgerManager.SaveSpeakerProfileAsync(aliceProfile);
        await _projectionService.ProjectSpeakerAsync(aliceProfile);

        // 4. Configure synthetic Pocket API client
        const string recordingId = "rec_pkt_e2e_001";
        const string recordingTitle = "Executive Planning & Architecture Sync";
        var createdAt = new DateTimeOffset(2026, 10, 18, 14, 0, 0, TimeSpan.Zero);
        byte[] validMp3Bytes = GenerateValidMp3Frames();
        var audioUri = new Uri($"https://s3.example.com/audio/{recordingId}.mp3");

        var pocketSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: createdAt,
            Title: recordingTitle,
            DurationSeconds: 120.0);

        var pocketRawMetadata = $$"""
            {
                "id": "{{recordingId}}",
                "title": "{{recordingTitle}}",
                "duration_seconds": 120.0,
                "created_at": "{{createdAt:O}}"
            }
            """;

        var mockPocketClient = Substitute.For<IPocketApiClient>();
        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { pocketSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(validMp3Bytes));
        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(pocketRawMetadata);

        // 5. Configure mock AudioWorkerClient for transcription & diarization
        var mockAudioWorker = Substitute.For<IAudioWorkerClient>();
        var transcriptionSegments = new List<TranscriptionSegment>
        {
            new()
            {
                StartMs = 0,
                EndMs = 5000,
                Speaker = "SPEAKER_00",
                Text = "Welcome Alice, let's review the AcmeCorp Platform CloudSync initiative."
            },
            new()
            {
                StartMs = 5100,
                EndMs = 10000,
                Speaker = "SPEAKER_01",
                Text = "Thanks. We have finalized the architecture and all tests are passing."
            }
        };
        mockAudioWorker.TranscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult
            {
                Text = "Welcome Alice, let's review the AcmeCorp Platform CloudSync initiative. Thanks. We have finalized the architecture and all tests are passing.",
                Segments = transcriptionSegments,
                Speakers =
                [
                    new RecordingSpeaker(
                        ClusterId: "SPEAKER_00",
                        SoloSnippet: new SoloSnippetBounds(0, 5000),
                        EmbeddingVector: aliceEmbedding),
                    new RecordingSpeaker(
                        ClusterId: "SPEAKER_01",
                        SoloSnippet: new SoloSnippetBounds(5100, 10000),
                        EmbeddingVector: CreateNormalizedEmbedding(512, seed: 99))
                ]
            });

        // 6. Configure SpeakerVoicebankService
        ISpeakerVoicebankService voicebankService = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);

        // 7. Configure mock TaxonomyClassifier
        var mockClassifier = Substitute.For<ITaxonomyClassifier>();
        mockClassifier.ClassifyRecordingAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClassificationResult(
                Domain: "Engineering",
                Customer: "AcmeCorp",
                Project: "Platform",
                Initiative: "CloudSync",
                Confidence: 0.92,
                TemplateId: "executive-summary",
                Rationale: "Explicit discussion of AcmeCorp Platform CloudSync initiative"));

        // 8. Configure mock TemplateSummarizer
        var mockSummarizer = Substitute.For<ITemplateSummarizer>();
        const string generatedSummary = """
            # Executive Summary: Executive Planning & Architecture Sync
            **Domain:** Engineering | **Customer:** AcmeCorp | **Project:** Platform
            **Speakers:** Alice Smith, SPEAKER_01

            ## Decisions
            - CloudSync initiative architecture approved.

            ## Next Steps
            - Deploy multi-machine coordination tests.
            """;
        mockSummarizer.GenerateSummaryAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<ClassificationResult>(), Arg.Any<CancellationToken>())
            .Returns(generatedSummary);

        // Assemble glue orchestration fixture
        var fixture = new FullPipelineGlueFixture(
            _vaultPathResolver,
            _ledgerManager,
            _sqliteContext,
            mockPocketClient,
            mockAudioWorker,
            voicebankService,
            mockClassifier,
            _tagger,
            _folderRouter,
            mockSummarizer,
            _projectionService);

        // Act
        var result = await fixture.ExecutePipelineAsync();

        // Assert: 1. Staging audio and metadata downloaded
        result.Should().NotBeNull();
        result.RecordingId.Should().Be(recordingId);
        File.Exists(result.StagingAudioPlatformPath).Should().BeTrue();
        File.Exists(result.StagingMetadataPlatformPath).Should().BeTrue();

        // Assert: 2. Audio container tagged via TagLibSharp
        using (var taggedAudio = TagLib.File.Create(result.StagingAudioPlatformPath))
        {
            taggedAudio.Tag.Title.Should().Be(recordingTitle);
            taggedAudio.Tag.Performers.Should().Contain("Alice Smith");
            taggedAudio.Tag.Album.Should().Be("Engineering");
        }

        // Assert: 3. Routed markdown document created in routed/
        result.RoutedSummaryPlatformPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.RoutedSummaryPlatformPath).Should().BeTrue();
        var writtenMarkdown = await File.ReadAllTextAsync(result.RoutedSummaryPlatformPath);
        writtenMarkdown.Should().Contain("CloudSync initiative architecture approved");

        // Assert: 4. Manifest persisted in ledger with Completed/Routed status
        var savedManifest = await _ledgerManager.GetRecordingManifestAsync(recordingId);
        savedManifest.Should().NotBeNull();
        savedManifest!.Status.Should().BeOneOf("Completed", "Routed");
        savedManifest.RoutedSummaryPath.Should().NotBeNull();
        savedManifest.RoutedSummaryPath.Should().StartWith("routed/Engineering/AcmeCorp/Platform/");
        savedManifest.Classification.Should().NotBeNull();
        savedManifest.Classification!.Domain.Should().Be("Engineering");
        savedManifest.Classification.Customer.Should().Be("AcmeCorp");
        savedManifest.Classification.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        savedManifest.Speakers.Should().NotBeNull();
        savedManifest.Speakers!.Should().Contain(s => s.DisplayName == "Alice Smith" && s.EnrolledSpeakerId == "spk_alice");

        // Assert: 5. Local SQLite index verified
        using var connection = _sqliteContext.CreateConnection();
        await connection.OpenAsync();

        var dbRecording = await connection.QuerySingleOrDefaultAsync<RecordingDbRow>(
            "SELECT id, status, domain, customer, project, initiative, confidence, template_id, routed_summary_path FROM recordings WHERE id = @Id;",
            new { Id = recordingId });

        dbRecording.Should().NotBeNull();
        dbRecording!.Status.Should().BeOneOf("Completed", "Routed");
        dbRecording.Domain.Should().Be("Engineering");
        dbRecording.Customer.Should().Be("AcmeCorp");
        dbRecording.Project.Should().Be("Platform");
        dbRecording.Initiative.Should().Be("CloudSync");
        dbRecording.Confidence.Should().BeApproximately(0.92, 0.01);
        dbRecording.TemplateId.Should().Be("executive-summary");
        dbRecording.RoutedSummaryPath.Should().Be(savedManifest.RoutedSummaryPath);

        var dbSpeakers = (await connection.QueryAsync<RecordingSpeakerDbRow>(
            "SELECT cluster_id, enrolled_speaker_id, display_name, match_confidence FROM recording_speakers WHERE recording_id = @Id;",
            new { Id = recordingId })).ToList();

        dbSpeakers.Should().NotBeEmpty();
        dbSpeakers.Should().Contain(s => s.DisplayName == "Alice Smith" && s.EnrolledSpeakerId == "spk_alice");
    }

    [Fact]
    public async Task ExecuteFullPipeline_WhenClassificationBelowThreshold_RoutesToReviewQueueAndUpdatesSqliteIndex()
    {
        // Arrange
        await _sqliteContext.InitializeDatabaseAsync();

        const string recordingId = "rec_pkt_low_conf_002";
        const string recordingTitle = "Vague Casual Chatter";
        var createdAt = new DateTimeOffset(2026, 10, 18, 15, 0, 0, TimeSpan.Zero);
        byte[] validMp3Bytes = GenerateValidMp3Frames();
        var audioUri = new Uri($"https://s3.example.com/audio/{recordingId}.mp3");

        var pocketSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: createdAt,
            Title: recordingTitle,
            DurationSeconds: 45.0);

        var mockPocketClient = Substitute.For<IPocketApiClient>();
        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { pocketSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(validMp3Bytes));
        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns($"{{\"id\":\"{recordingId}\",\"title\":\"{recordingTitle}\"}}");

        var mockAudioWorker = Substitute.For<IAudioWorkerClient>();
        mockAudioWorker.TranscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult
            {
                Text = "Just chatting about random weekend plans.",
                Segments = []
            });

        ISpeakerVoicebankService voicebankService = new SpeakerVoicebankService(_ledgerManager, _sqliteContext);

        var mockClassifier = Substitute.For<ITaxonomyClassifier>();
        mockClassifier.ClassifyRecordingAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClassificationResult(
                Domain: "Unknown",
                Customer: "General",
                Project: "Misc",
                Initiative: "None",
                Confidence: 0.45,
                TemplateId: "default",
                Rationale: "No domain match detected"));

        var mockSummarizer = Substitute.For<ITemplateSummarizer>();
        mockSummarizer.GenerateSummaryAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<ClassificationResult>(), Arg.Any<CancellationToken>())
            .Returns("# Summary\nCasual chatter without business domain classification.");

        var fixture = new FullPipelineGlueFixture(
            _vaultPathResolver,
            _ledgerManager,
            _sqliteContext,
            mockPocketClient,
            mockAudioWorker,
            voicebankService,
            mockClassifier,
            _tagger,
            _folderRouter,
            mockSummarizer,
            _projectionService);

        // Act
        var result = await fixture.ExecutePipelineAsync();

        // Assert
        result.Should().NotBeNull();
        result.FinalManifest.RoutedSummaryPath.Should().StartWith("_needs_review/");

        using var connection = _sqliteContext.CreateConnection();
        await connection.OpenAsync();

        var dbRecording = await connection.QuerySingleOrDefaultAsync<RecordingDbRow>(
            "SELECT id, status, domain, customer, project, initiative, confidence, template_id, routed_summary_path FROM recordings WHERE id = @Id;",
            new { Id = recordingId });

        dbRecording.Should().NotBeNull();
        dbRecording!.RoutedSummaryPath.Should().StartWith("_needs_review/");
    }

    private static byte[] GenerateValidMp3Frames()
    {
        using var ms = new MemoryStream();
        // 5 MPEG-1 Layer 3 frames, 44.1kHz, 128kbps, padded (418 bytes per frame)
        var frameHeader = new byte[] { 0xFF, 0xFB, 0x90, 0x64 };
        var frameData = new byte[418 - 4];
        for (var i = 0; i < frameData.Length; i++)
        {
            frameData[i] = (byte)(0x55 ^ (i & 0xFF));
        }

        for (var frame = 0; frame < 5; frame++)
        {
            ms.Write(frameHeader, 0, frameHeader.Length);
            ms.Write(frameData, 0, frameData.Length);
        }

        return ms.ToArray();
    }

    private static float[] CreateNormalizedEmbedding(int dimensions, int seed)
    {
        var random = new Random(seed);
        var vector = new float[dimensions];
        var sumSquares = 0.0;
        for (var i = 0; i < dimensions; i++)
        {
            vector[i] = (float)random.NextDouble();
            sumSquares += vector[i] * vector[i];
        }

        var magnitude = Math.Sqrt(sumSquares);
        for (var i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(vector[i] / magnitude);
        }

        return vector;
    }

    private sealed class RecordingDbRow
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public string? Customer { get; set; }
        public string? Project { get; set; }
        public string? Initiative { get; set; }
        public double? Confidence { get; set; }
        public string? TemplateId { get; set; }
        public string? RoutedSummaryPath { get; set; }
    }

    private sealed class RecordingSpeakerDbRow
    {
        public string ClusterId { get; set; } = string.Empty;
        public string? EnrolledSpeakerId { get; set; }
        public string? DisplayName { get; set; }
        public double MatchConfidence { get; set; }
    }
}
