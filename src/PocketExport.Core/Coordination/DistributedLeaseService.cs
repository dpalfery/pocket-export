using System.Text.Json;
using System.Text.Json.Serialization;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;

namespace PocketExport.Core.Coordination;

/// <summary>
/// Provides distributed lease coordination and exclusive item-level locking across nodes using the shared vault filesystem.
/// </summary>
public sealed class DistributedLeaseService : IDistributedLeaseService
{
    private static readonly TimeSpan ItemLockTtl = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IVaultPathResolver _vaultPathResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedLeaseService"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">Resolver for platform and vault-relative filesystem paths.</param>
    public DistributedLeaseService(IVaultPathResolver vaultPathResolver)
    {
        ArgumentNullException.ThrowIfNull(vaultPathResolver);
        _vaultPathResolver = vaultPathResolver;
    }

    /// <inheritdoc />
    public async Task<bool> AcquireLeaseAsync(string role, string nodeId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var relativePath = $".ledger/leases/{role}.json";
        var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);
        var now = DateTimeOffset.UtcNow;

        if (File.Exists(platformPath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(platformPath, cancellationToken);
                var existingLease = JsonSerializer.Deserialize<LeaseManifest>(existingJson, JsonOptions);

                if (existingLease is not null)
                {
                    if (existingLease.ExpiresAt > now && !string.Equals(existingLease.HolderId, nodeId, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
            catch (IOException)
            {
                // In event of concurrent access contention, consider active
                return false;
            }
            catch (JsonException)
            {
                // Corrupt file, allow re-acquisition
            }
        }

        var leaseManifest = new LeaseManifest(
            Role: role,
            HolderId: nodeId,
            AcquiredAt: now,
            ExpiresAt: now + ttl);

        await WriteLeaseManifestAtomicallyAsync(platformPath, leaseManifest, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RenewLeaseAsync(string role, string nodeId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var relativePath = $".ledger/leases/{role}.json";
        var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (!File.Exists(platformPath))
        {
            return false;
        }

        LeaseManifest? existingLease;
        try
        {
            var existingJson = await File.ReadAllTextAsync(platformPath, cancellationToken);
            existingLease = JsonSerializer.Deserialize<LeaseManifest>(existingJson, JsonOptions);
        }
        catch
        {
            return false;
        }

        if (existingLease is null || !string.Equals(existingLease.HolderId, nodeId, StringComparison.Ordinal))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var renewedLease = existingLease with
        {
            ExpiresAt = now + ttl
        };

        await WriteLeaseManifestAtomicallyAsync(platformPath, renewedLease, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task ReleaseLeaseAsync(string role, string nodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var relativePath = $".ledger/leases/{role}.json";
        var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (!File.Exists(platformPath))
        {
            return;
        }

        try
        {
            var existingJson = await File.ReadAllTextAsync(platformPath, cancellationToken);
            var existingLease = JsonSerializer.Deserialize<LeaseManifest>(existingJson, JsonOptions);

            if (existingLease is not null && !string.Equals(existingLease.HolderId, nodeId, StringComparison.Ordinal))
            {
                return;
            }

            File.Delete(platformPath);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsPrimaryWorkerActiveAsync(CancellationToken cancellationToken = default)
    {
        string[] candidateRoles = ["primary-worker", "primary_poller"];
        var now = DateTimeOffset.UtcNow;

        foreach (var role in candidateRoles)
        {
            var relativePath = $".ledger/leases/{role}.json";
            var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);

            if (!File.Exists(platformPath))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(platformPath, cancellationToken);
                var lease = JsonSerializer.Deserialize<LeaseManifest>(json, JsonOptions);

                if (lease is not null && lease.ExpiresAt > now)
                {
                    return true;
                }
            }
            catch
            {
                // Corrupt or transient error, continue checking
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> AcquireItemLockAsync(string recordingId, string nodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var relativePath = $".ledger/locks/{recordingId}.lock";
        var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        var dir = Path.GetDirectoryName(platformPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(platformPath))
        {
            try
            {
                var fileInfo = new FileInfo(platformPath);
                var lockAge = DateTimeOffset.UtcNow - fileInfo.LastWriteTimeUtc;
                if (lockAge > ItemLockTtl)
                {
                    try
                    {
                        File.Delete(platformPath);
                    }
                    catch
                    {
                        // In case another process deletes or races
                    }
                }
                else
                {
                    var currentHolder = await File.ReadAllTextAsync(platformPath, cancellationToken);
                    if (string.Equals(currentHolder.Trim(), nodeId.Trim(), StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return false;
                }
            }
            catch (IOException)
            {
                // File access contention
            }
        }

        try
        {
            await using var stream = new FileStream(platformPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(nodeId.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return true;
        }
        catch (IOException)
        {
            if (File.Exists(platformPath))
            {
                try
                {
                    var fileInfo = new FileInfo(platformPath);
                    var lockAge = DateTimeOffset.UtcNow - fileInfo.LastWriteTimeUtc;
                    if (lockAge > ItemLockTtl)
                    {
                        await File.WriteAllTextAsync(platformPath, nodeId, cancellationToken);
                        return true;
                    }

                    var currentHolder = await File.ReadAllTextAsync(platformPath, cancellationToken);
                    if (string.Equals(currentHolder.Trim(), nodeId.Trim(), StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Locked exclusively by another handle
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    public async Task ReleaseItemLockAsync(string recordingId, string nodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var relativePath = $".ledger/locks/{recordingId}.lock";
        var platformPath = _vaultPathResolver.ResolvePlatformPath(relativePath);

        if (!File.Exists(platformPath))
        {
            return;
        }

        try
        {
            var currentHolder = await File.ReadAllTextAsync(platformPath, cancellationToken);
            if (string.Equals(currentHolder.Trim(), nodeId.Trim(), StringComparison.Ordinal))
            {
                File.Delete(platformPath);
            }
        }
        catch
        {
            // Best effort release
        }
    }

    private static async Task WriteLeaseManifestAtomicallyAsync(string platformPath, LeaseManifest manifest, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(platformPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var tmpPath = Path.Combine(dir!, $"{Path.GetFileName(platformPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
            File.Move(tmpPath, platformPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }
}
