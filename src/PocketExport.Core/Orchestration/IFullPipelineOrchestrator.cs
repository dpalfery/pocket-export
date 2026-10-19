namespace PocketExport.Core.Orchestration;

using PocketExport.Core.Models;

/// <summary>
/// Execution result emitted by the full pipeline orchestrator for a single recording.
/// </summary>
public sealed record FullPipelineExecutionResult(
    string RecordingId,
    string StagingAudioPlatformPath,
    string StagingMetadataPlatformPath,
    string RoutedSummaryPlatformPath,
    RecordingManifest FinalManifest);

/// <summary>
/// Orchestrates the full recording processing pipeline end-to-end:
/// downloads from Pocket API, dispatches transcription and diarization,
/// matches speaker embeddings, classifies taxonomy, tags container metadata,
/// routes summary file, generates markdown summary, and updates ledger and SQLite index.
/// </summary>
public interface IFullPipelineOrchestrator
{
    /// <summary>
    /// Executes the full recording processing pipeline end-to-end for all pending recordings.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of pipeline execution results.</returns>
    Task<IReadOnlyList<FullPipelineExecutionResult>> ExecuteFullPipelineAsync(CancellationToken cancellationToken = default);
}
