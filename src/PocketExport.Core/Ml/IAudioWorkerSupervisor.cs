namespace PocketExport.Core.Ml;

/// <summary>
///     Lifecycle and execution states for an asynchronous transcription job.
/// </summary>
public enum TaskState
{
    /// <summary>Task is queued and awaiting worker assignment.</summary>
    Pending,

    /// <summary>Task is currently being processed by the worker.</summary>
    Processing,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Transcription failed due to a worker crash or execution error.</summary>
    TranscriptionFailed,

    /// <summary>Task failed due to an unrecoverable failure.</summary>
    Failed
}

/// <summary>
///     Tracks execution state, paths, and diagnostics for an individual audio transcription task.
/// </summary>
public sealed class TranscriptionTask
{
    /// <summary>
    ///     Gets or sets the recording identifier.
    /// </summary>
    public string RecordingId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the vault-relative audio file path.
    /// </summary>
    public string AudioPath { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the current processing state.
    /// </summary>
    public TaskState State { get; set; } = TaskState.Pending;

    /// <summary>
    ///     Gets or sets diagnostic failure details if processing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
///     Manages the lifecycle of the external audio worker process.
/// </summary>
public interface IWorkerProcessRunner
{
    /// <summary>
    ///     Launches the worker process and returns its operating system process ID.
    /// </summary>
    Task<int> LaunchWorkerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Terminates the specified worker process.
    /// </summary>
    Task TerminateProcessAsync(int pid, CancellationToken cancellationToken = default);
}

/// <summary>
///     Probes the responsiveness and health of the external audio worker.
/// </summary>
public interface IWorkerHealthClient
{
    /// <summary>
    ///     Checks if the worker is responsive and healthy.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Supervises the audio worker process lifecycle, memory consumption, and failure handling.
/// </summary>
public interface IAudioWorkerSupervisor
{
    /// <summary>
    ///     Gets the active worker process identifier, or null if no worker is running.
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    ///     Gets a value indicating whether the worker process is currently running and healthy.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Gets the most recent diagnostic error message from a process failure, or null.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    ///     Launches the worker process, validates its health check endpoint, and stores its PID.
    /// </summary>
    Task StartWorkerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Queries the worker health status.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enforces the supervisor memory threshold, restarting the worker if exceeded.
    /// </summary>
    Task EnforceMemoryLimitAsync(long currentMemoryBytes, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Handles an unexpected worker crash, recording diagnostics and updating task state.
    /// </summary>
    Task HandleWorkerCrashAsync(int pid, string stderrOutput, TranscriptionTask task, CancellationToken cancellationToken = default);
}
