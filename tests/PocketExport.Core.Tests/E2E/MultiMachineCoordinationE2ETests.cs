using System.Text.Json;
using FluentAssertions;
using PocketExport.Core.Coordination;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.E2E;

/// <summary>
/// Simulation result emitted by the multi-machine coordination glue orchestration fixture.
/// </summary>
public sealed record DualMachineSimulationResult(
    bool PrimaryLeaseAcquired,
    bool PrimaryLeaseRenewed,
    bool SecondaryPollingSuppressed,
    bool SecondaryStandbyTakeoverSucceeded,
    bool ItemLockContentionBlocked);

/// <summary>
/// Glue orchestration fixture simulating dual-machine coordination on a shared OneDrive VaultRoot:
/// primary node 60s lease acquisition with 20s renewal, secondary node polling suppression,
/// standby failover takeover on expiration, and item-specific locking.
/// </summary>
public sealed class MultiMachineCoordinationGlueFixture
{
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly IDistributedLeaseService _primaryLeaseService;
    private readonly IDistributedLeaseService _secondaryLeaseService;

    public MultiMachineCoordinationGlueFixture(
        IVaultPathResolver vaultPathResolver,
        IDistributedLeaseService primaryLeaseService,
        IDistributedLeaseService secondaryLeaseService)
    {
        _vaultPathResolver = vaultPathResolver;
        _primaryLeaseService = primaryLeaseService;
        _secondaryLeaseService = secondaryLeaseService;
    }

    /// <summary>
    /// Executes a simulated dual-machine coordination scenario across primary and secondary nodes.
    /// </summary>
    /// <param name="primaryNodeId">Identifier of the primary node.</param>
    /// <param name="secondaryNodeId">Identifier of the secondary node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A simulation result recording coordination events across both nodes.</returns>
    public async Task<DualMachineSimulationResult> ExecuteDualMachineCoordinationCycleAsync(
        string primaryNodeId,
        string secondaryNodeId,
        CancellationToken cancellationToken = default)
    {
        var primaryNode = new SimulatedMachineNode(primaryNodeId, _primaryLeaseService);
        var secondaryNode = new SimulatedMachineNode(secondaryNodeId, _secondaryLeaseService);

        // 1. Primary node acquires 60s lease
        var primaryAcquired = await primaryNode.AcquirePrimaryRoleAsync(TimeSpan.FromSeconds(60), cancellationToken);

        // 2. Primary node renews lease for 20s
        var primaryRenewed = await primaryNode.RenewPrimaryRoleAsync(TimeSpan.FromSeconds(20), cancellationToken);

        // 3. Secondary node polling cycle is suppressed while primary lease is active
        var secondaryPolled = await secondaryNode.ExecutePollingCycleAsync(cancellationToken);
        var secondarySuppressed = !secondaryPolled && secondaryNode.PollingSuppressed;

        // 4. Primary node crashes / lease expires: simulate expiration
        var expiredLease = new LeaseManifest(
            Role: "primary-worker",
            HolderId: primaryNodeId,
            AcquiredAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-15));

        var leasePlatformPath = _vaultPathResolver.ResolvePlatformPath(".ledger/leases/primary-worker.json");
        var expiredJson = JsonSerializer.Serialize(expiredLease, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        await File.WriteAllTextAsync(leasePlatformPath, expiredJson, cancellationToken);

        // 5. Secondary node detects expired lease and takes over primary role
        var secondaryTakeoverSucceeded = await secondaryNode.ExecutePollingCycleAsync(cancellationToken);
        var secondaryTakeover = secondaryTakeoverSucceeded && secondaryNode.IsPrimaryHolder;

        // 6. Item-specific locking isolation and contention prevention
        const string lockItemId = "rec_item_concurrency_sim";
        var primaryLocked = await primaryNode.TryLockRecordingItemAsync(lockItemId, cancellationToken);
        var secondaryBlocked = !await secondaryNode.TryLockRecordingItemAsync(lockItemId, cancellationToken);
        var itemContentionBlocked = primaryLocked && secondaryBlocked;
        await primaryNode.ReleaseRecordingItemAsync(lockItemId, cancellationToken);

        return new DualMachineSimulationResult(
            PrimaryLeaseAcquired: primaryAcquired,
            PrimaryLeaseRenewed: primaryRenewed,
            SecondaryPollingSuppressed: secondarySuppressed,
            SecondaryStandbyTakeoverSucceeded: secondaryTakeover,
            ItemLockContentionBlocked: itemContentionBlocked);
    }
}

/// <summary>
/// Represents a simulated machine node participating in distributed coordination over a shared VaultRoot.
/// </summary>
public sealed class SimulatedMachineNode
{
    public SimulatedMachineNode(string nodeId, IDistributedLeaseService leaseService)
    {
        NodeId = nodeId;
        LeaseService = leaseService;
    }

    public string NodeId { get; }

    public IDistributedLeaseService LeaseService { get; }

    public bool IsPrimaryHolder { get; private set; }

    public bool PollingSuppressed { get; private set; }

    public async Task<bool> AcquirePrimaryRoleAsync(TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var acquired = await LeaseService.AcquireLeaseAsync("primary-worker", NodeId, ttl, cancellationToken);
        IsPrimaryHolder = acquired;
        return acquired;
    }

    public async Task<bool> RenewPrimaryRoleAsync(TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (!IsPrimaryHolder)
        {
            return false;
        }

        var renewed = await LeaseService.RenewLeaseAsync("primary-worker", NodeId, ttl, cancellationToken);
        if (!renewed)
        {
            IsPrimaryHolder = false;
        }

        return renewed;
    }

    public async Task<bool> ExecutePollingCycleAsync(CancellationToken cancellationToken = default)
    {
        if (IsPrimaryHolder)
        {
            PollingSuppressed = false;
            return true;
        }

        var isPrimaryActive = await LeaseService.IsPrimaryWorkerActiveAsync(cancellationToken);
        if (isPrimaryActive)
        {
            PollingSuppressed = true;
            return false;
        }

        // Primary lease is not active: attempt standby takeover
        var acquired = await LeaseService.AcquireLeaseAsync("primary-worker", NodeId, TimeSpan.FromSeconds(60), cancellationToken);
        if (acquired)
        {
            IsPrimaryHolder = true;
            PollingSuppressed = false;
            return true;
        }

        PollingSuppressed = true;
        return false;
    }

    public async Task<bool> TryLockRecordingItemAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        return await LeaseService.AcquireItemLockAsync(recordingId, NodeId, cancellationToken);
    }

    public async Task ReleaseRecordingItemAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        await LeaseService.ReleaseItemLockAsync(recordingId, NodeId, cancellationToken);
    }
}

/// <summary>
/// End-to-end integration test contract specifying multi-machine coordination over a shared OneDrive VaultRoot:
/// primary node 60s lease acquisition and 20s renewals, secondary node polling suppression,
/// simulated lease expiration failover takeover, and per-item lock contention prevention.
/// </summary>
public sealed class MultiMachineCoordinationE2ETests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly IDistributedLeaseService _primaryLeaseService;
    private readonly IDistributedLeaseService _secondaryLeaseService;

    public MultiMachineCoordinationE2ETests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-coord-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);

        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();

        _primaryLeaseService = new DistributedLeaseService(_vaultPathResolver);
        _secondaryLeaseService = new DistributedLeaseService(_vaultPathResolver);
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
    public async Task PrimaryNode_Acquires60sLeaseAndRenewsEvery20s_MaintainsActivePrimaryLease()
    {
        // Arrange
        const string role = "primary-worker";
        const string primaryNodeId = "node-alpha-primary";
        var initialTtl = TimeSpan.FromSeconds(60);
        var renewalTtl = TimeSpan.FromSeconds(60);

        // Act: 1. Primary node acquires 60s lease
        var acquired = await _primaryLeaseService.AcquireLeaseAsync(role, primaryNodeId, initialTtl);

        // Assert: 1. Lease acquired and persisted to .ledger/leases/primary-worker.json
        acquired.Should().BeTrue();

        var leasePlatformPath = _vaultPathResolver.ResolvePlatformPath($".ledger/leases/{role}.json");
        File.Exists(leasePlatformPath).Should().BeTrue();

        var leaseJson = await File.ReadAllTextAsync(leasePlatformPath);
        var lease = JsonSerializer.Deserialize<LeaseManifest>(leaseJson, JsonOptions);
        lease.Should().NotBeNull();
        lease!.Role.Should().Be(role);
        lease.HolderId.Should().Be(primaryNodeId);
        lease.ExpiresAt.Should().BeCloseTo(lease.AcquiredAt + initialTtl, TimeSpan.FromSeconds(2));

        var initialExpiration = lease.ExpiresAt;

        // Act: 2. Primary node renews every 20s
        var renewed = await _primaryLeaseService.RenewLeaseAsync(role, primaryNodeId, renewalTtl);

        // Assert: 2. Expiration extended while maintaining holder identity
        renewed.Should().BeTrue();

        var renewedJson = await File.ReadAllTextAsync(leasePlatformPath);
        var renewedLease = JsonSerializer.Deserialize<LeaseManifest>(renewedJson, JsonOptions);
        renewedLease.Should().NotBeNull();
        renewedLease!.HolderId.Should().Be(primaryNodeId);
        renewedLease.ExpiresAt.Should().BeOnOrAfter(initialExpiration);
    }

    [Fact]
    public async Task SecondaryNode_WhenPrimaryLeaseActive_ObservesActiveLeaseAndSuppressesAutomatedPolling()
    {
        // Arrange
        const string primaryNodeId = "node-alpha-primary";
        const string secondaryNodeId = "node-beta-secondary";
        var primaryNode = new SimulatedMachineNode(primaryNodeId, _primaryLeaseService);
        var secondaryNode = new SimulatedMachineNode(secondaryNodeId, _secondaryLeaseService);

        // Primary acquires active lease
        var primaryAcquired = await primaryNode.AcquirePrimaryRoleAsync(TimeSpan.FromSeconds(60));
        primaryAcquired.Should().BeTrue();

        // Act: Secondary node attempts to run polling cycle
        var secondaryPolled = await secondaryNode.ExecutePollingCycleAsync();

        // Assert: Secondary node detects active primary lease and suppresses polling
        secondaryPolled.Should().BeFalse();
        secondaryNode.PollingSuppressed.Should().BeTrue();
        secondaryNode.IsPrimaryHolder.Should().BeFalse();

        // Verify active lease remains held by primary node
        var isActive = await _secondaryLeaseService.IsPrimaryWorkerActiveAsync();
        isActive.Should().BeTrue();
    }

    [Fact]
    public async Task SecondaryNode_WhenPrimaryLeaseExpires_AllowsSecondaryStandbyTakeover()
    {
        // Arrange
        const string role = "primary-worker";
        const string stalePrimaryId = "node-alpha-crashed";
        const string secondaryNodeId = "node-beta-secondary";

        // Seed expired lease manifest representing primary crash / lack of renewal > 60s
        var expiredLease = new LeaseManifest(
            Role: role,
            HolderId: stalePrimaryId,
            AcquiredAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-15));

        var leasePlatformPath = _vaultPathResolver.ResolvePlatformPath($".ledger/leases/{role}.json");
        var expiredJson = JsonSerializer.Serialize(expiredLease, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        await File.WriteAllTextAsync(leasePlatformPath, expiredJson);

        var secondaryNode = new SimulatedMachineNode(secondaryNodeId, _secondaryLeaseService);

        // Act: Secondary node runs polling cycle and detects expired lease
        var takeoverResult = await secondaryNode.ExecutePollingCycleAsync();

        // Assert: Secondary node successfully takes over primary lease
        takeoverResult.Should().BeTrue();
        secondaryNode.IsPrimaryHolder.Should().BeTrue();
        secondaryNode.PollingSuppressed.Should().BeFalse();

        var updatedJson = await File.ReadAllTextAsync(leasePlatformPath);
        var updatedLease = JsonSerializer.Deserialize<LeaseManifest>(updatedJson, JsonOptions);
        updatedLease.Should().NotBeNull();
        updatedLease!.HolderId.Should().Be(secondaryNodeId);
        updatedLease.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ConcurrentProcessing_WhenItemLockedByOneNode_BlocksOtherNodeFromProcessing()
    {
        // Arrange
        const string primaryNodeId = "node-alpha";
        const string secondaryNodeId = "node-beta";
        const string recordingId = "rec_item_concurrency_001";

        var primaryNode = new SimulatedMachineNode(primaryNodeId, _primaryLeaseService);
        var secondaryNode = new SimulatedMachineNode(secondaryNodeId, _secondaryLeaseService);

        // Act: 1. Primary node acquires item lock for processing
        var primaryLocked = await primaryNode.TryLockRecordingItemAsync(recordingId);

        // Assert: 1. Primary lock succeeded and lock file written
        primaryLocked.Should().BeTrue();

        var lockFilePath = _vaultPathResolver.ResolvePlatformPath($".ledger/locks/{recordingId}.lock");
        File.Exists(lockFilePath).Should().BeTrue();
        var lockContent = await File.ReadAllTextAsync(lockFilePath);
        lockContent.Should().Contain(primaryNodeId);

        // Act: 2. Secondary node attempts to acquire the same item lock concurrently
        var secondaryLocked = await secondaryNode.TryLockRecordingItemAsync(recordingId);

        // Assert: 2. Secondary node is blocked from processing item
        secondaryLocked.Should().BeFalse();

        // Act: 3. Primary node finishes and releases item lock
        await primaryNode.ReleaseRecordingItemAsync(recordingId);

        // Act: 4. Secondary node re-attempts item lock acquisition
        var secondaryReacquire = await secondaryNode.TryLockRecordingItemAsync(recordingId);

        // Assert: 4. Secondary node can now acquire the released item lock
        secondaryReacquire.Should().BeTrue();
        var reacquiredContent = await File.ReadAllTextAsync(lockFilePath);
        reacquiredContent.Should().Contain(secondaryNodeId);
    }

    [Fact]
    public async Task DualMachineCoordination_GlueFixture_SimulatesDualMachineCoordinationAcrossPrimaryAndSecondaryNodes()
    {
        // Arrange
        const string primaryNodeId = "node-alpha-primary";
        const string secondaryNodeId = "node-beta-secondary";

        var fixture = new MultiMachineCoordinationGlueFixture(
            _vaultPathResolver,
            _primaryLeaseService,
            _secondaryLeaseService);

        // Act
        var simulationResult = await fixture.ExecuteDualMachineCoordinationCycleAsync(primaryNodeId, secondaryNodeId);

        // Assert
        simulationResult.Should().NotBeNull();
        simulationResult.PrimaryLeaseAcquired.Should().BeTrue();
        simulationResult.PrimaryLeaseRenewed.Should().BeTrue();
        simulationResult.SecondaryPollingSuppressed.Should().BeTrue();
        simulationResult.SecondaryStandbyTakeoverSucceeded.Should().BeTrue();
        simulationResult.ItemLockContentionBlocked.Should().BeTrue();
    }
}
