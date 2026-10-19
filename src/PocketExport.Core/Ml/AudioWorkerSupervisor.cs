namespace PocketExport.Core.Ml;

/// <summary>
///     Supervises the ML audio worker process lifecycle, health monitoring, memory bounds, and crash recovery.
/// </summary>
public sealed class AudioWorkerSupervisor : IAudioWorkerSupervisor
{
    private const long DefaultMemoryLimitBytes = 16L * 1024 * 1024 * 1024; // 16 GiB

    private readonly IWorkerProcessRunner _processRunner;
    private readonly IWorkerHealthClient _healthClient;
    private readonly long _memoryLimitBytes;

    private int? _processId;
    private bool _isRunning;
    private string? _lastError;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioWorkerSupervisor"/> class.
    /// </summary>
    /// <param name="processRunner">Runner used to launch and terminate the worker process.</param>
    /// <param name="healthClient">Client used to probe worker responsiveness.</param>
    /// <param name="memoryLimitBytes">Maximum permissible memory consumption before restarting the worker.</param>
    public AudioWorkerSupervisor(
        IWorkerProcessRunner processRunner,
        IWorkerHealthClient healthClient,
        long memoryLimitBytes = DefaultMemoryLimitBytes)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _healthClient = healthClient ?? throw new ArgumentNullException(nameof(healthClient));
        _memoryLimitBytes = memoryLimitBytes;
    }

    /// <inheritdoc />
    public int? ProcessId => _processId;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public async Task StartWorkerAsync(CancellationToken cancellationToken = default)
    {
        _processId = await _processRunner.LaunchWorkerAsync(cancellationToken);
        _isRunning = await _healthClient.CheckHealthAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var healthy = await _healthClient.CheckHealthAsync(cancellationToken);
        _isRunning = healthy;
        return healthy;
    }

    /// <inheritdoc />
    public async Task EnforceMemoryLimitAsync(long currentMemoryBytes, CancellationToken cancellationToken = default)
    {
        if (currentMemoryBytes <= _memoryLimitBytes)
        {
            return;
        }

        if (_processId.HasValue)
        {
            await _processRunner.TerminateProcessAsync(_processId.Value, cancellationToken);
        }

        await StartWorkerAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleWorkerCrashAsync(
        int pid,
        string stderrOutput,
        TranscriptionTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        _lastError = stderrOutput;
        _isRunning = false;

        task.State = TaskState.TranscriptionFailed;
        task.ErrorMessage = stderrOutput;

        return Task.CompletedTask;
    }
}
