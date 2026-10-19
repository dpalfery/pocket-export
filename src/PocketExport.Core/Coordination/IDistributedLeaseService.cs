namespace PocketExport.Core.Coordination;

/// <summary>
/// Provides distributed lease coordination and item-level locking across nodes using the shared vault filesystem.
/// </summary>
public interface IDistributedLeaseService
{
    /// <summary>
    /// Attempts to acquire a role lease for a given node with a time-to-live duration.
    /// </summary>
    /// <param name="role">The unique role identifier (e.g. primary-worker).</param>
    /// <param name="nodeId">The unique node identifier requesting the lease.</param>
    /// <param name="ttl">The duration for which the lease remains valid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the lease was successfully acquired; otherwise, <c>false</c>.</returns>
    Task<bool> AcquireLeaseAsync(string role, string nodeId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an active role lease if the caller is the current active holder.
    /// </summary>
    /// <param name="role">The unique role identifier.</param>
    /// <param name="nodeId">The unique node identifier renewing the lease.</param>
    /// <param name="ttl">The additional duration to extend the lease expiration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the lease was renewed; otherwise, <c>false</c>.</returns>
    Task<bool> RenewLeaseAsync(string role, string nodeId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a held role lease so that standby nodes can acquire it immediately.
    /// </summary>
    /// <param name="role">The unique role identifier.</param>
    /// <param name="nodeId">The unique node identifier releasing the lease.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReleaseLeaseAsync(string role, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the primary worker or poller is currently active with an unexpired lease.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the primary worker is active; otherwise, <c>false</c>.</returns>
    Task<bool> IsPrimaryWorkerActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically acquires an exclusive lock for processing a specific recording item.
    /// </summary>
    /// <param name="recordingId">The recording identifier.</param>
    /// <param name="nodeId">The unique node identifier acquiring the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the item lock was acquired; otherwise, <c>false</c>.</returns>
    Task<bool> AcquireItemLockAsync(string recordingId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an exclusive item lock.
    /// </summary>
    /// <param name="recordingId">The recording identifier.</param>
    /// <param name="nodeId">The unique node identifier releasing the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReleaseItemLockAsync(string recordingId, string nodeId, CancellationToken cancellationToken = default);
}
