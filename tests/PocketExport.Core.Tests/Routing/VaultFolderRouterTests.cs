using FluentAssertions;
using PocketExport.Core.Models;
using PocketExport.Core.Routing;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Routing;

/// <summary>
/// Unit tests specifying the routing contract for vault folder destination paths and collision handling.
/// </summary>
public sealed class VaultFolderRouterTests : IDisposable
{
    private readonly string _vaultRoot;
    private readonly IVaultPathResolver _pathResolver;

    public VaultFolderRouterTests()
    {
        _vaultRoot = Path.Combine(Path.GetTempPath(), "pocket-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultRoot);
        _pathResolver = new VaultPathResolver(_vaultRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultRoot))
        {
            try
            {
                Directory.Delete(_vaultRoot, recursive: true);
            }
            catch
            {
                // Best-effort directory cleanup in test teardown
            }
        }
    }

    [Fact]
    public void DetermineRoutedPath_WhenConfidenceMeetsThreshold_RoutesToDomainCustomerProjectHierarchy()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "AcmeCorp",
            Project: "MobileApp",
            Initiative: "Q4Migration",
            Confidence: 0.85,
            TemplateId: "meeting");
        const string recordingId = "rec_123";
        var createdAt = new DateTime(2026, 10, 18, 14, 30, 0, DateTimeKind.Utc);
        const string titleSlug = "kickoff-meeting";

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenConfidenceIsExactlyThreshold_RoutesToDomainCustomerProjectHierarchy()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Product",
            Customer: "BetaClients",
            Project: "SearchRevamp",
            Initiative: "Discovery",
            Confidence: 0.75,
            TemplateId: "spec");
        const string recordingId = "rec_456";
        var createdAt = new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);
        const string titleSlug = "architecture-sync";

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("routed/Product/BetaClients/SearchRevamp/2026-05-04_rec_456_architecture-sync.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenConfidenceIsBelowThreshold_RoutesToNeedsReview()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "AcmeCorp",
            Project: "MobileApp",
            Initiative: "Q4Migration",
            Confidence: 0.749,
            TemplateId: "meeting");
        const string recordingId = "rec_789";
        var createdAt = new DateTime(2026, 10, 18, 8, 15, 0, DateTimeKind.Utc);
        const string titleSlug = "low-confidence-discussion";

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("_needs_review/2026-10-18_rec_789_low-confidence-discussion.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenConfidenceIsZero_RoutesToNeedsReview()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Unknown",
            Customer: "Unknown",
            Project: "Unknown",
            Initiative: "Unknown",
            Confidence: 0.0,
            TemplateId: "default");
        const string recordingId = "rec_000";
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const string titleSlug = "unclassified-audio";

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("_needs_review/2026-01-01_rec_000_unclassified-audio.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenTargetFileAlreadyExists_AppendsMonotonicSuffix1()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "AcmeCorp",
            Project: "MobileApp",
            Initiative: "Q4Migration",
            Confidence: 0.90,
            TemplateId: "meeting");
        const string recordingId = "rec_123";
        var createdAt = new DateTime(2026, 10, 18, 14, 30, 0, DateTimeKind.Utc);
        const string titleSlug = "kickoff-meeting";

        const string existingRelativePath = "routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting.md";
        var fullPath = _pathResolver.ResolvePlatformPath(existingRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "# Existing Document");

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting_1.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenTargetAndFirstSuffixExist_AppendsMonotonicSuffix2()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "AcmeCorp",
            Project: "MobileApp",
            Initiative: "Q4Migration",
            Confidence: 0.90,
            TemplateId: "meeting");
        const string recordingId = "rec_123";
        var createdAt = new DateTime(2026, 10, 18, 14, 30, 0, DateTimeKind.Utc);
        const string titleSlug = "kickoff-meeting";

        const string baseRelativePath = "routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting.md";
        const string firstSuffixRelativePath = "routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting_1.md";

        var basePath = _pathResolver.ResolvePlatformPath(baseRelativePath);
        var firstSuffixPath = _pathResolver.ResolvePlatformPath(firstSuffixRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        File.WriteAllText(basePath, "# Original Document");
        File.WriteAllText(firstSuffixPath, "# First Collided Document");

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("routed/Engineering/AcmeCorp/MobileApp/2026-10-18_rec_123_kickoff-meeting_2.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenNeedsReviewFileExists_AppendsMonotonicSuffix()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "AcmeCorp",
            Project: "MobileApp",
            Initiative: "Q4Migration",
            Confidence: 0.50,
            TemplateId: "meeting");
        const string recordingId = "rec_999";
        var createdAt = new DateTime(2026, 10, 18, 14, 30, 0, DateTimeKind.Utc);
        const string titleSlug = "review-meeting";

        const string baseRelativePath = "_needs_review/2026-10-18_rec_999_review-meeting.md";
        var basePath = _pathResolver.ResolvePlatformPath(baseRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        File.WriteAllText(basePath, "# Existing Review Document");

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().Be("_needs_review/2026-10-18_rec_999_review-meeting_1.md");
    }

    [Fact]
    public void DetermineRoutedPath_WhenSegmentsContainSpecialCharacters_SanitizesPathSegments()
    {
        // Arrange
        IVaultFolderRouter router = new VaultFolderRouter(_pathResolver);
        var classification = new ClassificationResult(
            Domain: "Engineering:Core",
            Customer: "Acme/Corp",
            Project: "Mobile*App",
            Initiative: "Q4|Goal",
            Confidence: 0.85,
            TemplateId: "meeting");
        const string recordingId = "rec_special";
        var createdAt = new DateTime(2026, 10, 18, 14, 30, 0, DateTimeKind.Utc);
        const string titleSlug = "bad?slug|test";

        // Act
        var routedPath = router.DetermineRoutedPath(classification, recordingId, createdAt, titleSlug);

        // Assert
        routedPath.Should().NotContainAny(":", "*", "?", "|");
        routedPath.Should().StartWith("routed/");
        routedPath.Should().EndWith(".md");
    }
}
