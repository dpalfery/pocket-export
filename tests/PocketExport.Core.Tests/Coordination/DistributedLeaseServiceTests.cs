using System.Text.Json;
using FluentAssertions;
using PocketExport.Core.Coordination;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Coordination;

/// <summary>
///     Unit and integration contract tests specifying distributed role leasing and per-item work locking.
/// </summary>
public sealed class DistributedLeaseServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;

    public DistributedLeaseServiceTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-lease-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
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
                // Best-effort cleanup of test temp directory
            }
        }
    }

    [Fact]
    public async Task AcquireLeaseAsync_WhenNoActiveLease_WritesLeaseManifestWith60sExpirationAndReturnsTrue()
    {
        // Arrange
        IDistributedLeaseService sut = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";
        const string nodeId = "node-alpha";
        var ttl = TimeSpan.FromSeconds(60);
        var beforeAcquire = DateTimeOffset.UtcNow;

        // Act
        var acquired = await sut.AcquireLeaseAsync(role, nodeId, ttl);

        // Assert
        acquired.Should().BeTrue();

        var leaseFilePath = _vaultPathResolver.ResolvePlatformPath($".ledger/leases/{role}.json");
        File.Exists(leaseFilePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(leaseFilePath);
        var lease = JsonSerializer.Deserialize<LeaseManifest>(json, JsonOptions);
        lease.Should().NotBeNull();
        lease!.Role.Should().Be(role);
        lease.HolderId.Should().Be(nodeId);
        lease.AcquiredAt.Should().BeOnOrAfter(beforeAcquire);
        lease.ExpiresAt.Should().BeCloseTo(lease.AcquiredAt + ttl, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AcquireLeaseAsync_WhenActiveLeaseHeldByAnotherNode_ReturnsFalse()
    {
        // Arrange
        IDistributedLeaseService primarySut = new DistributedLeaseService(_vaultPathResolver);
        IDistributedLeaseService standbySut = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";
        await primarySut.AcquireLeaseAsync(role, "node-alpha", TimeSpan.FromSeconds(60));

        // Act
        var acquired = await standbySut.AcquireLeaseAsync(role, "node-beta", TimeSpan.FromSeconds(60));

        // Assert
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task RenewLeaseAsync_WhenHoldingActiveLease_UpdatesExpirationTimestamp()
    {
        // Arrange
        IDistributedLeaseService sut = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";
        const string nodeId = "node-alpha";
        await sut.AcquireLeaseAsync(role, nodeId, TimeSpan.FromSeconds(60));

        var leaseFilePath = _vaultPathResolver.ResolvePlatformPath($".ledger/leases/{role}.json");
        var initialJson = await File.ReadAllTextAsync(leaseFilePath);
        var initialLease = JsonSerializer.Deserialize<LeaseManifest>(initialJson, JsonOptions);
        initialLease.Should().NotBeNull();

        var renewalTtl = TimeSpan.FromSeconds(60);

        // Act
        var renewed = await sut.RenewLeaseAsync(role, nodeId, renewalTtl);

        // Assert
        renewed.Should().BeTrue();

        var updatedJson = await File.ReadAllTextAsync(leaseFilePath);
        var updatedLease = JsonSerializer.Deserialize<LeaseManifest>(updatedJson, JsonOptions);
        updatedLease.Should().NotBeNull();
        updatedLease!.HolderId.Should().Be(nodeId);
        updatedLease.ExpiresAt.Should().BeOnOrAfter(initialLease!.ExpiresAt);
    }

    [Fact]
    public async Task RenewLeaseAsync_WhenNotCurrentHolder_ReturnsFalseAndDoesNotModifyLease()
    {
        // Arrange
        IDistributedLeaseService sut = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";
        await sut.AcquireLeaseAsync(role, "node-alpha", TimeSpan.FromSeconds(60));

        // Act
        var renewed = await sut.RenewLeaseAsync(role, "imposter-node", TimeSpan.FromSeconds(20));

        // Assert
        renewed.Should().BeFalse();
    }

    [Fact]
    public async Task IsPrimaryWorkerActiveAsync_WhenActiveLeaseExists_ReturnsTrueToSuppressSecondaryNode()
    {
        // Arrange
        IDistributedLeaseService primarySut = new DistributedLeaseService(_vaultPathResolver);
        IDistributedLeaseService secondarySut = new DistributedLeaseService(_vaultPathResolver);
        await primarySut.AcquireLeaseAsync("primary-worker", "primary-gpu-node", TimeSpan.FromSeconds(60));

        // Act
        var isActive = await secondarySut.IsPrimaryWorkerActiveAsync();

        // Assert
        isActive.Should().BeTrue();
    }

    [Fact]
    public async Task IsPrimaryWorkerActiveAsync_WhenNoLeaseExists_ReturnsFalse()
    {
        // Arrange
        IDistributedLeaseService sut = new DistributedLeaseService(_vaultPathResolver);

        // Act
        var isActive = await sut.IsPrimaryWorkerActiveAsync();

        // Assert
        isActive.Should().BeFalse();
    }

    [Fact]
    public async Task AcquireLeaseAsync_WhenExistingLeaseExpired_AllowsStandbyNodeToClaimLease()
    {
        // Arrange
        IDistributedLeaseService standbySut = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";

        var expiredLease = new LeaseManifest(
            Role: role,
            HolderId: "stale-node",
            AcquiredAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-10));

        var leaseFilePath = _vaultPathResolver.ResolvePlatformPath($".ledger/leases/{role}.json");
        var json = JsonSerializer.Serialize(expiredLease, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        await File.WriteAllTextAsync(leaseFilePath, json);

        // Act
        var acquired = await standbySut.AcquireLeaseAsync(role, "standby-node", TimeSpan.FromSeconds(60));

        // Assert
        acquired.Should().BeTrue();

        var updatedJson = await File.ReadAllTextAsync(leaseFilePath);
        var updatedLease = JsonSerializer.Deserialize<LeaseManifest>(updatedJson, JsonOptions);
        updatedLease.Should().NotBeNull();
        updatedLease!.HolderId.Should().Be("standby-node");
        updatedLease.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AcquireItemLockAsync_WhenUnlocked_WritesLockFileAndReturnsTrue()
    {
        // Arrange
        IDistributedLeaseService sut = new DistributedLeaseService(_vaultPathResolver);
        const string recordingId = "rec_2026_item_001";
        const string nodeId = "worker-node-1";

        // Act
        var locked = await sut.AcquireItemLockAsync(recordingId, nodeId);

        // Assert
        locked.Should().BeTrue();

        var lockFilePath = _vaultPathResolver.ResolvePlatformPath($".ledger/locks/{recordingId}.lock");
        File.Exists(lockFilePath).Should().BeTrue();

        var lockContent = await File.ReadAllTextAsync(lockFilePath);
        lockContent.Should().Contain(nodeId);
    }

    [Fact]
    public async Task AcquireItemLockAsync_WhenAlreadyLocked_RejectsConcurrentAcquisition()
    {
        // Arrange
        IDistributedLeaseService sut1 = new DistributedLeaseService(_vaultPathResolver);
        IDistributedLeaseService sut2 = new DistributedLeaseService(_vaultPathResolver);
        const string recordingId = "rec_2026_item_002";

        var firstLock = await sut1.AcquireItemLockAsync(recordingId, "node-alpha");
        firstLock.Should().BeTrue();

        // Act
        var secondLock = await sut2.AcquireItemLockAsync(recordingId, "node-beta");

        // Assert
        secondLock.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseItemLockAsync_WhenLockHeld_ReleasesLockAllowingReacquisition()
    {
        // Arrange
        IDistributedLeaseService sut1 = new DistributedLeaseService(_vaultPathResolver);
        IDistributedLeaseService sut2 = new DistributedLeaseService(_vaultPathResolver);
        const string recordingId = "rec_2026_item_003";

        await sut1.AcquireItemLockAsync(recordingId, "node-alpha");

        // Act
        await sut1.ReleaseItemLockAsync(recordingId, "node-alpha");
        var reacquired = await sut2.AcquireItemLockAsync(recordingId, "node-beta");

        // Assert
        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseLeaseAsync_WhenHoldingLease_ReleasesLeaseAllowingOtherNodeToAcquire()
    {
        // Arrange
        IDistributedLeaseService sut1 = new DistributedLeaseService(_vaultPathResolver);
        IDistributedLeaseService sut2 = new DistributedLeaseService(_vaultPathResolver);
        const string role = "primary-worker";
        await sut1.AcquireLeaseAsync(role, "node-alpha", TimeSpan.FromSeconds(60));

        // Act
        await sut1.ReleaseLeaseAsync(role, "node-alpha");
        var acquired = await sut2.AcquireLeaseAsync(role, "node-beta", TimeSpan.FromSeconds(60));

        // Assert
        acquired.Should().BeTrue();
    }
}
