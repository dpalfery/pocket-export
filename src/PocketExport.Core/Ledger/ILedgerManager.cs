namespace PocketExport.Core.Ledger;

using PocketExport.Core.Models;

/// <summary>
/// Manages atomic filesystem operations and persistence for the shared state ledger.
/// </summary>
public interface ILedgerManager
{
    /// <summary>
    /// Atomically saves a recording manifest to <c>.ledger/recordings/{id}.json</c> with concurrency retry.
    /// </summary>
    /// <param name="manifest">The recording manifest to persist.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task SaveRecordingManifestAsync(RecordingManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a recording manifest by identifier.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The recording manifest if found; otherwise, <see langword="null"/>.</returns>
    Task<RecordingManifest?> GetRecordingManifestAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all recording manifests stored in the ledger.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of all recording manifests.</returns>
    Task<IReadOnlyList<RecordingManifest>> GetAllRecordingManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a recording manifest by identifier.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task DeleteRecordingManifestAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically saves a speaker profile to <c>.ledger/speakers/{id}.json</c> with concurrency retry.
    /// </summary>
    /// <param name="profile">The speaker profile to persist.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task SaveSpeakerProfileAsync(SpeakerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a speaker profile by identifier.
    /// </summary>
    /// <param name="id">The unique speaker profile identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The speaker profile if found; otherwise, <see langword="null"/>.</returns>
    Task<SpeakerProfile?> GetSpeakerProfileAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all speaker profiles stored in the ledger.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of all speaker profiles.</returns>
    Task<IReadOnlyList<SpeakerProfile>> GetAllSpeakerProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a speaker profile by identifier.
    /// </summary>
    /// <param name="id">The unique speaker identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task DeleteSpeakerProfileAsync(string id, CancellationToken cancellationToken = default);
}
