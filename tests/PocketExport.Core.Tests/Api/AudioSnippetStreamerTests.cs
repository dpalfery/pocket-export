using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PocketExport.Api.Controllers;
using PocketExport.Api.Services;
using PocketExport.Core.Database;
using PocketExport.Core.Ledger;
using PocketExport.Core.Ml;
using PocketExport.Core.Models;
using PocketExport.Core.Routing;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using Xunit;

namespace PocketExport.Core.Tests.Api;

/// <summary>
/// Unit and integration test contract specifying <see cref="AudioSnippetStreamer"/> and the
/// snippet streaming HTTP endpoint (<c>GET /api/recordings/{id}/speakers/{index}/snippet</c>).
/// Asserts HTTP 206 Partial Content with Content-Range byte slices for [start_ms, end_ms].
/// </summary>
public sealed class AudioSnippetStreamerTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly IVaultFolderRouter _vaultFolderRouter;
    private readonly IAudioWorkerClient _audioWorkerClient;
    private readonly ITemplateSummarizer _templateSummarizer;
    private readonly SqliteIndexContext _sqliteContext;
    private readonly IAudioSnippetStreamer _streamer;

    public AudioSnippetStreamerTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-snippet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);

        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();

        _ledgerManager = Substitute.For<ILedgerManager>();
        _vaultFolderRouter = Substitute.For<IVaultFolderRouter>();
        _audioWorkerClient = Substitute.For<IAudioWorkerClient>();
        _templateSummarizer = Substitute.For<ITemplateSummarizer>();

        var dbPath = Path.Combine(_testVaultDir, "local-index.db");
        _sqliteContext = new SqliteIndexContext(dbPath);

        _streamer = new AudioSnippetStreamer(_vaultPathResolver, _ledgerManager);
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
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task GetSpeakerSnippet_WhenValidRecordingAndSoloSnippet_ReturnsHttp206WithContentRangeHeader()
    {
        // Arrange
        const string recordingId = "rec_snippet_1";
        const int speakerIndex = 0;
        const long startMs = 1000;
        const long endMs = 3000;

        // Create dummy staging audio file with known byte pattern
        var audioRelativePath = $"staging/{recordingId}/audio.wav";
        var fullAudioPath = _vaultPathResolver.ResolveAbsolutePath(audioRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullAudioPath)!);
        var audioBytes = new byte[320000]; // simulate ~10 seconds of 16kHz 16-bit mono PCM
        for (var i = 0; i < audioBytes.Length; i++)
        {
            audioBytes[i] = (byte)(i % 256);
        }
        await File.WriteAllBytesAsync(fullAudioPath, audioBytes);

        var recording = new RecordingManifest(
            Id: recordingId,
            PocketId: "pkt_1",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Snippet Test Recording",
            DurationSeconds: 10.0,
            StagingAudioPath: audioRelativePath,
            Status: "completed",
            Speakers: new List<RecordingSpeaker>
            {
                new(
                    ClusterId: "SPEAKER_00",
                    DisplayName: "Alice",
                    MatchConfidence: 0.9,
                    SoloSnippet: new SoloSnippetBounds(startMs, endMs))
            });

        _ledgerManager.GetRecordingManifestAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(recording);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetSpeakerSnippetAsync(recordingId, speakerIndex, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // Must return HTTP 206 Partial Content
        var httpContext = controller.ControllerContext.HttpContext;
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);

        // Content-Range header must be present and formatted as "bytes {start}-{end}/{total}"
        httpContext.Response.Headers.Should().ContainKey("Content-Range");
        var contentRange = httpContext.Response.Headers["Content-Range"].ToString();
        contentRange.Should().StartWith("bytes ");
        contentRange.Should().Contain("/");

        // Accept-Ranges header must be "bytes"
        httpContext.Response.Headers.Should().ContainKey("Accept-Ranges");
        httpContext.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public async Task GetSpeakerSnippet_WhenRecordingNotFound_ReturnsNotFound()
    {
        // Arrange
        _ledgerManager.GetRecordingManifestAsync("rec_missing", Arg.Any<CancellationToken>())
            .Returns((RecordingManifest?)null);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetSpeakerSnippetAsync("rec_missing", 0, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSpeakerSnippet_WhenSpeakerIndexOutOfBounds_ReturnsNotFound()
    {
        // Arrange
        const string recordingId = "rec_single_speaker";
        var recording = new RecordingManifest(
            Id: recordingId,
            PocketId: "pkt_2",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Single Speaker",
            DurationSeconds: 5.0,
            StagingAudioPath: $"staging/{recordingId}/audio.wav",
            Status: "completed",
            Speakers: new List<RecordingSpeaker>
            {
                new("SPEAKER_00", SoloSnippet: new SoloSnippetBounds(500, 1500))
            });

        _ledgerManager.GetRecordingManifestAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(recording);

        var controller = CreateController();

        // Act - Request speaker at index 3 (only index 0 exists)
        IActionResult result = await controller.GetSpeakerSnippetAsync(recordingId, 3, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSpeakerSnippet_WhenSpeakerHasNoSoloSnippet_ReturnsNotFound()
    {
        // Arrange
        const string recordingId = "rec_no_snippet";
        var recording = new RecordingManifest(
            Id: recordingId,
            PocketId: "pkt_3",
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "No Snippet Speaker",
            DurationSeconds: 5.0,
            StagingAudioPath: $"staging/{recordingId}/audio.wav",
            Status: "completed",
            Speakers: new List<RecordingSpeaker>
            {
                new("SPEAKER_00", SoloSnippet: null)
            });

        _ledgerManager.GetRecordingManifestAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(recording);

        var controller = CreateController();

        // Act
        IActionResult result = await controller.GetSpeakerSnippetAsync(recordingId, 0, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void CalculateByteRange_WhenValidTimeRange_CalculatesAccurateByteSliceOffsets()
    {
        // Arrange
        const long totalBytes = 320000;
        const double durationSeconds = 10.0;
        const long startMs = 2000; // 2 seconds in
        const long endMs = 4000;   // 4 seconds in

        // Act
        var range = AudioSnippetStreamer.CalculateByteRange(totalBytes, durationSeconds, startMs, endMs);

        // Assert
        // Expected byte offsets proportionally: 2s / 10s = 20%, 4s / 10s = 40%
        range.StartByte.Should().Be(64000);
        range.EndByte.Should().Be(128000);
        range.TotalBytes.Should().Be(totalBytes);
        range.Length.Should().Be(range.EndByte - range.StartByte + 1);
    }

    [Fact]
    public void CalculateByteRange_WhenStartMsGreaterThanEndMs_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => AudioSnippetStreamer.CalculateByteRange(
            totalBytes: 100000,
            durationSeconds: 10.0,
            startMs: 5000,
            endMs: 2000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*startMs must be less than or equal to endMs*");
    }

    [Fact]
    public void CalculateByteRange_WhenStartMsIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => AudioSnippetStreamer.CalculateByteRange(
            totalBytes: 100000,
            durationSeconds: 10.0,
            startMs: -500,
            endMs: 2000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*startMs cannot be negative*");
    }

    [Fact]
    public async Task StreamSnippetAsync_WhenValidBounds_WritesExactByteRangeToStream()
    {
        // Arrange
        var testFilePath = Path.Combine(_testVaultDir, "test_audio.raw");
        var rawData = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        await File.WriteAllBytesAsync(testFilePath, rawData);

        using var memoryStream = new MemoryStream();

        // Act
        await _streamer.StreamSnippetToAsync(
            filePath: testFilePath,
            startByte: 10,
            endByte: 19,
            destination: memoryStream,
            cancellationToken: CancellationToken.None);

        // Assert
        var writtenBytes = memoryStream.ToArray();
        var writtenString = Encoding.UTF8.GetString(writtenBytes);
        writtenString.Should().Be("ABCDEFGHIJ");
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
            _streamer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }
}
