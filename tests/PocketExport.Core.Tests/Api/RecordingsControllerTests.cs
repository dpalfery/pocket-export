using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PocketExport.Api.Controllers;
using PocketExport.Api.Models;
using PocketExport.Api.Services;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Ml;
using PocketExport.Core.Models;
using PocketExport.Core.Routing;
using PocketExport.Core.Speakers;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using Xunit;

namespace PocketExport.Core.Tests.Api;

/// <summary>
/// Unit and integration test contract specifying the HTTP API endpoints of <see cref="RecordingsController"/>
/// including list filtering, recording retrieval, taxonomy retagging, worker re-transcription, and re-summarization.
/// </summary>
public sealed class RecordingsControllerTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly IVaultFolderRouter _vaultFolderRouter;
    private readonly IAudioWorkerClient _audioWorkerClient;
    private readonly ITemplateSummarizer _templateSummarizer;
    private readonly IAudioSnippetStreamer _audioSnippetStreamer;
    private readonly SqliteIndexContext _sqliteContext;

    public RecordingsControllerTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-recordings-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);

        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();

        _ledgerManager = Substitute.For<ILedgerManager>();
        _vaultFolderRouter = Substitute.For<IVaultFolderRouter>();
        _audioWorkerClient = Substitute.For<IAudioWorkerClient>();
        _templateSummarizer = Substitute.For<ITemplateSummarizer>();
        _audioSnippetStreamer = Substitute.For<IAudioSnippetStreamer>();

        var dbPath = Path.Combine(_testVaultDir, "local-index.db");
        _sqliteContext = new SqliteIndexContext(dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _sqliteContext.Dispose();

        if (Directory.Exists(_testVaultDir))
        {
            try
            {
                Directory.Delete(_testVaultDir, recursive: true);
            }
            catch
            {
                // Best-effort directory cleanup in test teardown
            }
        }
    }

    [Fact]
    public async Task GetRecordings_WhenNoFiltersApplied_ReturnsAllRecordings()
    {
        // Arrange
        var recordings = new List<RecordingManifest>
        {
            CreateSampleRecording("rec_1", "Engineering Meeting", "Engineering", "Acme", "completed"),
            CreateSampleRecording("rec_2", "Sales Pitch", "Sales", "Globex", "completed"),
            CreateSampleRecording("rec_3", "Incident Review", "Operations", "Initech", "failed")
        };

        _ledgerManager.GetAllRecordingManifestsAsync(Arg.Any<CancellationToken>())
            .Returns(recordings);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingsAsync(
            domain: null,
            customer: null,
            status: null,
            search: null,
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedRecordings = okResult.Value.Should().BeAssignableTo<IEnumerable<RecordingManifest>>().Subject;
        returnedRecordings.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecordings_WhenFilteredByDomain_ReturnsOnlyMatchingDomain()
    {
        // Arrange
        var recordings = new List<RecordingManifest>
        {
            CreateSampleRecording("rec_1", "Engineering Meeting", "Engineering", "Acme", "completed"),
            CreateSampleRecording("rec_2", "Sales Pitch", "Sales", "Globex", "completed")
        };

        _ledgerManager.GetAllRecordingManifestsAsync(Arg.Any<CancellationToken>())
            .Returns(recordings);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingsAsync(
            domain: "Engineering",
            customer: null,
            status: null,
            search: null,
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedRecordings = okResult.Value.Should().BeAssignableTo<IEnumerable<RecordingManifest>>().Subject;
        returnedRecordings.Should().ContainSingle()
            .Which.Classification!.Domain.Should().Be("Engineering");
    }

    [Fact]
    public async Task GetRecordings_WhenFilteredByCustomer_ReturnsOnlyMatchingCustomer()
    {
        // Arrange
        var recordings = new List<RecordingManifest>
        {
            CreateSampleRecording("rec_1", "Engineering Meeting", "Engineering", "Acme", "completed"),
            CreateSampleRecording("rec_2", "Sales Pitch", "Sales", "Globex", "completed")
        };

        _ledgerManager.GetAllRecordingManifestsAsync(Arg.Any<CancellationToken>())
            .Returns(recordings);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingsAsync(
            domain: null,
            customer: "Globex",
            status: null,
            search: null,
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedRecordings = okResult.Value.Should().BeAssignableTo<IEnumerable<RecordingManifest>>().Subject;
        returnedRecordings.Should().ContainSingle()
            .Which.Classification!.Customer.Should().Be("Globex");
    }

    [Fact]
    public async Task GetRecordings_WhenFilteredByStatus_ReturnsOnlyMatchingStatus()
    {
        // Arrange
        var recordings = new List<RecordingManifest>
        {
            CreateSampleRecording("rec_1", "Engineering Meeting", "Engineering", "Acme", "completed"),
            CreateSampleRecording("rec_2", "Audio Syncing", "Engineering", "Acme", "processing"),
            CreateSampleRecording("rec_3", "Failed Job", "Engineering", "Acme", "failed")
        };

        _ledgerManager.GetAllRecordingManifestsAsync(Arg.Any<CancellationToken>())
            .Returns(recordings);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingsAsync(
            domain: null,
            customer: null,
            status: "failed",
            search: null,
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedRecordings = okResult.Value.Should().BeAssignableTo<IEnumerable<RecordingManifest>>().Subject;
        returnedRecordings.Should().ContainSingle()
            .Which.Status.Should().Be("failed");
    }

    [Fact]
    public async Task GetRecordings_WhenFilteredBySearchText_ReturnsRecordingsMatchingTitle()
    {
        // Arrange
        var recordings = new List<RecordingManifest>
        {
            CreateSampleRecording("rec_1", "Quarterly Budget Sync", "Finance", "Acme", "completed"),
            CreateSampleRecording("rec_2", "Sprint Retrospective", "Engineering", "Acme", "completed")
        };

        _ledgerManager.GetAllRecordingManifestsAsync(Arg.Any<CancellationToken>())
            .Returns(recordings);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingsAsync(
            domain: null,
            customer: null,
            status: null,
            search: "Budget",
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedRecordings = okResult.Value.Should().BeAssignableTo<IEnumerable<RecordingManifest>>().Subject;
        returnedRecordings.Should().ContainSingle()
            .Which.Title.Should().Be("Quarterly Budget Sync");
    }

    [Fact]
    public async Task GetRecordingById_WhenRecordingExists_ReturnsOkWithDetailObject()
    {
        // Arrange
        var recording = CreateSampleRecording("rec_123", "Deep Dive", "Architecture", "Acme", "completed");
        _ledgerManager.GetRecordingManifestAsync("rec_123", Arg.Any<CancellationToken>())
            .Returns(recording);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingByIdAsync("rec_123", CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = okResult.Value.Should().BeAssignableTo<RecordingManifest>().Subject;
        detail.Id.Should().Be("rec_123");
        detail.Title.Should().Be("Deep Dive");
    }

    [Fact]
    public async Task GetRecordingById_WhenRecordingDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        _ledgerManager.GetRecordingManifestAsync("rec_missing", Arg.Any<CancellationToken>())
            .Returns((RecordingManifest?)null);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetRecordingByIdAsync("rec_missing", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Retag_WhenRecordingExists_UpdatesTaxonomyMetadataAndMovesFiles()
    {
        // Arrange
        var original = CreateSampleRecording("rec_123", "Roadmap Review", "OldDomain", "OldCustomer", "completed");
        _ledgerManager.GetRecordingManifestAsync("rec_123", Arg.Any<CancellationToken>())
            .Returns(original);

        var retagRequest = new RetagRecordingRequest(
            Domain: "Product",
            Customer: "NewCustomer",
            Project: "V2Launch",
            Initiative: "Q4",
            TemplateId: "executive");

        _vaultFolderRouter.DetermineRoutedPath(
                Arg.Any<ClassificationResult>(),
                Arg.Is("rec_123"),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>())
            .Returns("routed/Product/NewCustomer/V2Launch/2026-10-18_rec_123_roadmap-review.md");

        var controller = CreateController();

        // Act
        IActionResult result = await controller.RetagAsync("rec_123", retagRequest, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        await _ledgerManager.Received(1).SaveRecordingManifestAsync(
            Arg.Is<RecordingManifest>(m =>
                m.Id == "rec_123" &&
                m.Classification != null &&
                m.Classification.Domain == "Product" &&
                m.Classification.Customer == "NewCustomer" &&
                m.Classification.Project == "V2Launch" &&
                m.Classification.Initiative == "Q4" &&
                m.Classification.TemplateId == "executive"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retag_WhenRecordingDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        _ledgerManager.GetRecordingManifestAsync("rec_missing", Arg.Any<CancellationToken>())
            .Returns((RecordingManifest?)null);

        var retagRequest = new RetagRecordingRequest("Product", "NewCustomer", "V2Launch", "Q4");
        var controller = CreateController();

        // Act
        IActionResult result = await controller.RetagAsync("rec_missing", retagRequest, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
        await _ledgerManager.DidNotReceive().SaveRecordingManifestAsync(Arg.Any<RecordingManifest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retranscribe_WhenRecordingExists_TriggersWorkerRetranscription()
    {
        // Arrange
        var recording = CreateSampleRecording("rec_123", "Customer Call", "Sales", "Acme", "completed");
        _ledgerManager.GetRecordingManifestAsync("rec_123", Arg.Any<CancellationToken>())
            .Returns(recording);

        var transcriptionResult = new TranscriptionResult
        {
            Text = "Updated transcript text",
            Segments = new List<TranscriptionSegment>
            {
                new() { StartMs = 0, EndMs = 1500, Text = "Hello", Speaker = "SPEAKER_00" }
            }
        };

        _audioWorkerClient.TranscribeAsync(recording.StagingAudioPath, Arg.Any<CancellationToken>())
            .Returns(transcriptionResult);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.RetranscribeAsync("rec_123", CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IActionResult>();
        await _audioWorkerClient.Received(1).TranscribeAsync(recording.StagingAudioPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retranscribe_WhenRecordingDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        _ledgerManager.GetRecordingManifestAsync("rec_missing", Arg.Any<CancellationToken>())
            .Returns((RecordingManifest?)null);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.RetranscribeAsync("rec_missing", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
        await _audioWorkerClient.DidNotReceive().TranscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resummarize_WhenRecordingExists_TriggersResummarizationAndUpdatesSummary()
    {
        // Arrange
        var recording = CreateSampleRecording("rec_123", "Strategy Meeting", "Executive", "Acme", "completed");
        _ledgerManager.GetRecordingManifestAsync("rec_123", Arg.Any<CancellationToken>())
            .Returns(recording);

        _templateSummarizer.GenerateSummaryAsync(
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<ClassificationResult>(),
                Arg.Any<CancellationToken>())
            .Returns("# Strategy Summary\n\n- Key decision made.");

        var controller = CreateController();

        // Act
        IActionResult result = await controller.ResummarizeAsync("rec_123", CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IActionResult>();
        await _templateSummarizer.Received(1).GenerateSummaryAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>?>(),
            Arg.Any<ClassificationResult>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resummarize_WhenRecordingDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        _ledgerManager.GetRecordingManifestAsync("rec_missing", Arg.Any<CancellationToken>())
            .Returns((RecordingManifest?)null);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.ResummarizeAsync("rec_missing", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
        await _templateSummarizer.DidNotReceive().GenerateSummaryAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>?>(),
            Arg.Any<ClassificationResult>(),
            Arg.Any<CancellationToken>());
    }

    private RecordingsController CreateController()
    {
        var httpContext = new DefaultHttpContext();
        return new RecordingsController(
            _ledgerManager,
            _sqliteContext,
            _vaultPathResolver,
            _vaultFolderRouter,
            _audioWorkerClient,
            _templateSummarizer,
            _audioSnippetStreamer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static RecordingManifest CreateSampleRecording(
        string id,
        string title,
        string domain,
        string customer,
        string status)
    {
        return new RecordingManifest(
            Id: id,
            PocketId: "pkt_" + id,
            CreatedAt: DateTimeOffset.UtcNow,
            Title: title,
            DurationSeconds: 120.5,
            StagingAudioPath: $"staging/{id}/audio.wav",
            PocketMetadataPath: $"staging/{id}/metadata.json",
            RoutedTranscriptPath: $"routed/{domain}/{customer}/transcript.md",
            RoutedSummaryPath: $"routed/{domain}/{customer}/summary.md",
            Status: status,
            Speakers: new List<RecordingSpeaker>
            {
                new(
                    ClusterId: "SPEAKER_00",
                    DisplayName: "Alice",
                    MatchConfidence: 0.95,
                    SoloSnippet: new SoloSnippetBounds(1000, 3000))
            },
            Classification: new ClassificationResult(
                Domain: domain,
                Customer: customer,
                Project: "Alpha",
                Initiative: "Q4",
                Confidence: 0.92,
                TemplateId: "default"));
    }
}

/// <summary>
/// Unit and integration test contract specifying the HTTP API endpoints of <see cref="SpeakersController"/>
/// including speaker enrollment and batch approval of proposed retroactive matches.
/// </summary>
public sealed class SpeakersControllerTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly ISpeakerVoicebankService _voicebankService;
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _sqliteContext;

    public SpeakersControllerTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-speakers-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);

        _voicebankService = Substitute.For<ISpeakerVoicebankService>();
        _ledgerManager = Substitute.For<ILedgerManager>();

        var dbPath = Path.Combine(_testVaultDir, "local-index.db");
        _sqliteContext = new SqliteIndexContext(dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _sqliteContext.Dispose();

        if (Directory.Exists(_testVaultDir))
        {
            try
            {
                Directory.Delete(_testVaultDir, recursive: true);
            }
            catch
            {
                // Best-effort directory cleanup in test teardown
            }
        }
    }

    [Fact]
    public async Task Enroll_WhenValidPayload_EnrollsSpeakerAndUpdatesLedgerAndSqlite()
    {
        // Arrange
        var embedding = new float[512];
        Array.Fill(embedding, 0.05f);

        var bounds = new SoloSnippetBounds(1500, 4500);
        var request = new EnrollSpeakerRequest(
            Name: "Dr. Evelyn Reed",
            Organization: "Acme Bio",
            Embedding: embedding,
            RecordingId: "rec_source_1",
            Bounds: bounds);

        var enrolledProfile = new SpeakerProfile(
            Id: "spk_evelyn_reed",
            DisplayName: "Dr. Evelyn Reed",
            Organization: "Acme Bio",
            EnrolledAt: DateTimeOffset.UtcNow,
            SourceRecordingId: "rec_source_1",
            SampleSnippetBounds: bounds,
            Embedding: embedding);

        _voicebankService.EnrollSpeakerAsync(
                request.Name,
                request.Organization,
                request.Embedding,
                request.RecordingId,
                request.Bounds,
                Arg.Any<CancellationToken>())
            .Returns(enrolledProfile);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.EnrollAsync(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var profile = okResult.Value.Should().BeAssignableTo<SpeakerProfile>().Subject;
        profile.DisplayName.Should().Be("Dr. Evelyn Reed");
        profile.Organization.Should().Be("Acme Bio");

        await _voicebankService.Received(1).EnrollSpeakerAsync(
            request.Name,
            request.Organization,
            request.Embedding,
            request.RecordingId,
            request.Bounds,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enroll_WhenPayloadMissingName_ReturnsBadRequest()
    {
        // Arrange
        var request = new EnrollSpeakerRequest(
            Name: string.Empty,
            Organization: "Acme Bio",
            Embedding: new float[512],
            RecordingId: "rec_1",
            Bounds: new SoloSnippetBounds(0, 1000));

        var controller = CreateController();

        // Act
        IActionResult result = await controller.EnrollAsync(request, CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        await _voicebankService.DidNotReceive().EnrollSpeakerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<float[]>(),
            Arg.Any<string>(),
            Arg.Any<SoloSnippetBounds>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveRetroactiveMatches_WhenMatchIdsProvided_ApprovesBatchAndUpdatesDatabase()
    {
        // Arrange
        var request = new ApproveRetroactiveMatchesRequest(
            MatchIds: new List<long> { 101, 102, 103 });

        var controller = CreateController();

        // Act
        IActionResult result = await controller.ApproveRetroactiveMatchesAsync(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task ApproveRetroactiveMatches_WhenMatchIdsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var request = new ApproveRetroactiveMatchesRequest(MatchIds: new List<long>());
        var controller = CreateController();

        // Act
        IActionResult result = await controller.ApproveRetroactiveMatchesAsync(request, CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private SpeakersController CreateController()
    {
        var httpContext = new DefaultHttpContext();
        return new SpeakersController(_voicebankService, _ledgerManager, _sqliteContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }
}
