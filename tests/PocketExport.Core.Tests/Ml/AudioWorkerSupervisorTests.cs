using System.Net;
using System.Text;
using FluentAssertions;
using NSubstitute;
using PocketExport.Core.Ml;
using Xunit;

namespace PocketExport.Core.Tests.Ml;

/// <summary>
///     Unit tests specifying the behavioral contract for the audio ML worker supervisor and client.
/// </summary>
public sealed class AudioWorkerSupervisorTests
{
    [Fact]
    public async Task StartWorkerAsync_WhenInvoked_LaunchesProcessPollsHealthCheckAndStoresPid()
    {
        // Arrange
        var processRunner = Substitute.For<IWorkerProcessRunner>();
        var healthClient = Substitute.For<IWorkerHealthClient>();
        const int expectedPid = 42100;

        processRunner.LaunchWorkerAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedPid));
        healthClient.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        IAudioWorkerSupervisor sut = new AudioWorkerSupervisor(processRunner, healthClient);

        // Act
        await sut.StartWorkerAsync();

        // Assert
        sut.ProcessId.Should().Be(expectedPid);
        sut.IsRunning.Should().BeTrue();
        await processRunner.Received(1).LaunchWorkerAsync(Arg.Any<CancellationToken>());
        await healthClient.Received().CheckHealthAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_WhenWorkerIsResponsive_ReturnsTrue()
    {
        // Arrange
        var processRunner = Substitute.For<IWorkerProcessRunner>();
        var healthClient = Substitute.For<IWorkerHealthClient>();
        healthClient.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        IAudioWorkerSupervisor sut = new AudioWorkerSupervisor(processRunner, healthClient);

        // Act
        var isHealthy = await sut.CheckHealthAsync();

        // Assert
        isHealthy.Should().BeTrue();
        await healthClient.Received(1).CheckHealthAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckHealthAsync_WhenWorkerIsUnresponsive_ReturnsFalse()
    {
        // Arrange
        var processRunner = Substitute.For<IWorkerProcessRunner>();
        var healthClient = Substitute.For<IWorkerHealthClient>();
        healthClient.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        IAudioWorkerSupervisor sut = new AudioWorkerSupervisor(processRunner, healthClient);

        // Act
        var isHealthy = await sut.CheckHealthAsync();

        // Assert
        isHealthy.Should().BeFalse();
        await healthClient.Received(1).CheckHealthAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnforceMemoryLimitAsync_WhenMemoryExceeds16Gb_TerminatesAndRestartsProcess()
    {
        // Arrange
        var processRunner = Substitute.For<IWorkerProcessRunner>();
        var healthClient = Substitute.For<IWorkerHealthClient>();
        const int initialPid = 5001;
        const int restartedPid = 5002;

        processRunner.LaunchWorkerAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(initialPid), Task.FromResult(restartedPid));
        healthClient.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        IAudioWorkerSupervisor sut = new AudioWorkerSupervisor(processRunner, healthClient);
        await sut.StartWorkerAsync();

        const long memoryUsageExceeding16Gb = 17L * 1024 * 1024 * 1024; // 17 GiB in bytes

        // Act
        await sut.EnforceMemoryLimitAsync(memoryUsageExceeding16Gb);

        // Assert
        await processRunner.Received(1).TerminateProcessAsync(initialPid, Arg.Any<CancellationToken>());
        sut.ProcessId.Should().Be(restartedPid);
    }

    [Fact]
    public async Task TranscribeAsync_WithValidAudioPath_DispatchesHttpRequestAndDeserializesResult()
    {
        // Arrange
        var httpHandler = new MockHttpMessageHandler(
            """
            {
                "text": "Hello world from Pocket AI",
                "segments": [
                    {
                        "start_ms": 0,
                        "end_ms": 1500,
                        "text": "Hello world",
                        "speaker": "SPEAKER_00"
                    }
                ]
            }
            """);
        using var httpClient = new HttpClient(httpHandler)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };

        IAudioWorkerClient client = new AudioWorkerClient(httpClient);

        // Act
        var result = await client.TranscribeAsync("staging/raw_audio/recording_1.m4a");

        // Assert
        result.Should().NotBeNull();
        result.Text.Should().Be("Hello world from Pocket AI");
        result.Segments.Should().HaveCount(1);
        result.Segments[0].Speaker.Should().Be("SPEAKER_00");
    }

    [Fact]
    public async Task HandleWorkerCrashAsync_WhenWorkerCrashesWithStderr_CapturesDiagnosticsAndUpdatesTaskState()
    {
        // Arrange
        var processRunner = Substitute.For<IWorkerProcessRunner>();
        var healthClient = Substitute.For<IWorkerHealthClient>();
        const int pid = 5500;
        const string stderrOutput = "CUDA out of memory error in pyannote diarization";

        processRunner.LaunchWorkerAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(pid));
        healthClient.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        IAudioWorkerSupervisor sut = new AudioWorkerSupervisor(processRunner, healthClient);
        await sut.StartWorkerAsync();

        var task = new TranscriptionTask
        {
            RecordingId = "rec_123",
            AudioPath = "staging/raw_audio/rec_123.m4a",
            State = TaskState.Processing
        };

        // Act
        await sut.HandleWorkerCrashAsync(pid, stderrOutput, task);

        // Assert
        task.State.Should().Be(TaskState.TranscriptionFailed);
        task.ErrorMessage.Should().Contain(stderrOutput);
        sut.LastError.Should().Be(stderrOutput);
    }

    private sealed class MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
