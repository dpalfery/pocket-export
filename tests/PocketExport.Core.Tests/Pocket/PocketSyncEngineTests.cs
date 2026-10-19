using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using NSubstitute;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Pocket;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Pocket;

/// <summary>
/// Unit and contract tests specifying the Pocket API client contract and Pocket sync engine
/// synchronization, streaming, atomic ledger staging, retry, and fault tolerance behaviors.
/// </summary>
public sealed class PocketSyncEngineTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;

    public PocketSyncEngineTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
        _ledgerManager = Substitute.For<ILedgerManager>();
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
                // Best-effort directory cleanup in test teardown
            }
        }
    }

    [Fact]
    public async Task ListRecordingsAsync_WhenInvoked_QueriesRecordingsEndpointWithBearerToken()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://public.heypocketai.com/api/v1/")
        };
        const string bearerToken = "test_bearer_token_12345";
        IPocketApiClient client = new PocketApiClient(httpClient, bearerToken);

        // Act
        _ = await client.ListRecordingsAsync();

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri.Should().Be(new Uri("https://public.heypocketai.com/api/v1/public/recordings"));
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be(bearerToken);
    }

    [Fact]
    public async Task GetAudioUrlAsync_WhenInvoked_QueriesPreSignedAudioUrlEndpointWithBearerToken()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        const string recordingId = "rec_pkt_001";
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"audio_url\":\"https://s3.example.com/audio.m4a\"}", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://public.heypocketai.com/api/v1/")
        };
        const string bearerToken = "test_bearer_token_12345";
        IPocketApiClient client = new PocketApiClient(httpClient, bearerToken);

        // Act
        var audioUrl = await client.GetAudioUrlAsync(recordingId);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri.Should().Be(new Uri($"https://public.heypocketai.com/api/v1/public/recordings/{recordingId}/audio-url"));
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be(bearerToken);
        audioUrl.Should().Be(new Uri("https://s3.example.com/audio.m4a"));
    }

    [Fact]
    public async Task GetRecordingDetailAsync_WhenInvoked_QueriesDetailEndpointWithTranscriptAndSummarizations()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        const string recordingId = "rec_pkt_002";
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"rec_pkt_002\"}", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://public.heypocketai.com/api/v1/")
        };
        const string bearerToken = "test_bearer_token_12345";
        IPocketApiClient client = new PocketApiClient(httpClient, bearerToken);

        // Act
        _ = await client.GetRecordingDetailAsync(recordingId);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be($"/api/v1/public/recordings/{recordingId}");
        capturedRequest.RequestUri.Query.Should().Contain("include_transcript=true");
        capturedRequest.RequestUri.Query.Should().Contain("include_summarizations=true");
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be(bearerToken);
    }

    [Fact]
    public async Task SyncRecordingsAsync_WhenNewRecordingAvailable_StreamsAudioToStagingWithContentLengthVerification()
    {
        // Arrange
        var mockPocketClient = Substitute.For<IPocketApiClient>();
        const string recordingId = "rec_pkt_003";
        byte[] expectedAudioBytes = Encoding.UTF8.GetBytes("MOCK_AUDIO_DATA_CONTENT_1234567890");
        var audioUri = new Uri("https://s3.example.com/audio/rec_pkt_003.m4a");

        var recordingSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Team Standup",
            DurationSeconds: 120.5);

        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { recordingSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(expectedAudioBytes));
        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns("{\"id\":\"rec_pkt_003\",\"title\":\"Team Standup\"}");

        var engine = new PocketSyncEngine(mockPocketClient, _vaultPathResolver, _ledgerManager);

        // Act
        await engine.SyncRecordingsAsync();

        // Assert
        string expectedStagingAudioPath = _vaultPathResolver.ResolvePlatformPath($"staging/{recordingId}.m4a");
        File.Exists(expectedStagingAudioPath).Should().BeTrue();
        new FileInfo(expectedStagingAudioPath).Length.Should().Be(expectedAudioBytes.Length);
    }

    [Fact]
    public async Task SyncRecordingsAsync_WhenMetadataRetrieved_WritesVerbatimCloudMetadataToStagingJson()
    {
        // Arrange
        var mockPocketClient = Substitute.For<IPocketApiClient>();
        const string recordingId = "rec_pkt_004";
        const string verbatimJson = "{\"id\":\"rec_pkt_004\",\"title\":\"Product Review\",\"custom_payload\":{\"key\":\"value\"}}";
        byte[] audioBytes = Encoding.UTF8.GetBytes("AUDIO_BINARY_BYTES");
        var audioUri = new Uri("https://s3.example.com/audio/rec_pkt_004.m4a");

        var recordingSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Product Review",
            DurationSeconds: 60.0);

        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { recordingSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(audioBytes));
        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(verbatimJson);

        var engine = new PocketSyncEngine(mockPocketClient, _vaultPathResolver, _ledgerManager);

        // Act
        await engine.SyncRecordingsAsync();

        // Assert
        string stagingMetadataPath = _vaultPathResolver.ResolvePlatformPath($"staging/{recordingId}.pocket.json");
        File.Exists(stagingMetadataPath).Should().BeTrue();
        string writtenJson = await File.ReadAllTextAsync(stagingMetadataPath);
        writtenJson.Should().Be(verbatimJson);
    }

    [Fact]
    public async Task SyncRecordingsAsync_WhenSyncSucceeds_CreatesAtomicRecordingManifestWithStagedStatusInLedger()
    {
        // Arrange
        var mockPocketClient = Substitute.For<IPocketApiClient>();
        const string recordingId = "rec_pkt_005";
        var createdAt = DateTimeOffset.UtcNow;
        byte[] audioBytes = Encoding.UTF8.GetBytes("AUDIO_BINARY_BYTES");
        var audioUri = new Uri("https://s3.example.com/audio/rec_pkt_005.m4a");

        var recordingSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: createdAt,
            Title: "Architecture Sync",
            DurationSeconds: 180.0);

        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { recordingSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(audioBytes));
        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns("{\"id\":\"rec_pkt_005\",\"title\":\"Architecture Sync\"}");

        var engine = new PocketSyncEngine(mockPocketClient, _vaultPathResolver, _ledgerManager);

        // Act
        await engine.SyncRecordingsAsync();

        // Assert
        await _ledgerManager.Received(1).SaveRecordingManifestAsync(
            Arg.Is<RecordingManifest>(m =>
                m.Id == recordingId &&
                m.PocketId == recordingId &&
                m.Title == "Architecture Sync" &&
                m.Status == "Staged" &&
                m.StagingAudioPath == $"staging/{recordingId}.m4a" &&
                m.PocketMetadataPath == $"staging/{recordingId}.pocket.json"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SyncRecordingsAsync_WhenTransientHttp429Or5xxErrorOccurs_AppliesPollyRetryWithExponentialBackoff(HttpStatusCode transientStatusCode)
    {
        // Arrange
        var mockPocketClient = Substitute.For<IPocketApiClient>();
        const string recordingId = "rec_pkt_006";
        var audioUri = new Uri("https://s3.example.com/audio/rec_pkt_006.m4a");
        byte[] audioBytes = Encoding.UTF8.GetBytes("AUDIO_RETRY_BYTES");

        var recordingSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Retry Scenario",
            DurationSeconds: 45.0);

        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { recordingSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);

        int callCount = 0;
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new HttpRequestException($"Transient error {transientStatusCode}", null, transientStatusCode);
                }
                return new MemoryStream(audioBytes);
            });

        mockPocketClient.GetRawRecordingDetailJsonAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns("{\"id\":\"rec_pkt_006\"}");

        var engine = new PocketSyncEngine(mockPocketClient, _vaultPathResolver, _ledgerManager);

        // Act
        await engine.SyncRecordingsAsync();

        // Assert
        callCount.Should().BeGreaterThanOrEqualTo(2);
        string stagingAudioPath = _vaultPathResolver.ResolvePlatformPath($"staging/{recordingId}.m4a");
        File.Exists(stagingAudioPath).Should().BeTrue();
    }

    [Fact]
    public async Task SyncRecordingsAsync_WhenAudioDownloadFailsOrContentLengthCorrupted_DiscardsPartialFileAndDoesNotCommitManifest()
    {
        // Arrange
        var mockPocketClient = Substitute.For<IPocketApiClient>();
        const string recordingId = "rec_pkt_007";
        var audioUri = new Uri("https://s3.example.com/audio/rec_pkt_007.m4a");

        var recordingSummary = new PocketRecordingSummary(
            Id: recordingId,
            CreatedAt: DateTimeOffset.UtcNow,
            Title: "Corrupted Download",
            DurationSeconds: 90.0);

        mockPocketClient.ListRecordingsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PocketRecordingSummary> { recordingSummary });
        mockPocketClient.GetAudioUrlAsync(recordingId, Arg.Any<CancellationToken>())
            .Returns(audioUri);

        // Stream throws an exception mid-download after partial write or has mismatched length
        var faultyStream = new FaultyStream(new byte[] { 1, 2, 3, 4, 5 }, failAfterBytes: 3);
        mockPocketClient.DownloadAudioStreamAsync(audioUri, Arg.Any<CancellationToken>())
            .Returns(faultyStream);

        var engine = new PocketSyncEngine(mockPocketClient, _vaultPathResolver, _ledgerManager);

        // Act
        var act = () => engine.SyncRecordingsAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
        string stagingAudioPath = _vaultPathResolver.ResolvePlatformPath($"staging/{recordingId}.m4a");
        File.Exists(stagingAudioPath).Should().BeFalse("Partial or corrupted audio files must be cleaned up on failure");

        await _ledgerManager.DidNotReceive().SaveRecordingManifestAsync(
            Arg.Is<RecordingManifest>(m => m.Id == recordingId),
            Arg.Any<CancellationToken>());
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sendFunc;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sendFunc)
        {
            _sendFunc = sendFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_sendFunc(request));
        }
    }

    private sealed class FaultyStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _failAfterBytes;
        private int _position;

        public FaultyStream(byte[] data, int failAfterBytes)
        {
            _data = data;
            _failAfterBytes = failAfterBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _failAfterBytes)
            {
                throw new IOException("Simulated network stream interruption or content corruption.");
            }

            int toCopy = Math.Min(count, _failAfterBytes - _position);
            Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
