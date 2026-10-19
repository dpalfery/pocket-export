using FluentAssertions;
using PocketExport.Core.Storage;
using Xunit;

namespace PocketExport.Core.Tests.Storage;

public sealed class VaultPathResolverTests : IDisposable
{
    private readonly string _testVaultDir;

    public VaultPathResolverTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
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
    public void Constructor_WhenInstantiated_ImplementsIVaultPathResolver()
    {
        // Arrange & Act
        var resolver = new VaultPathResolver(_testVaultDir);

        // Assert
        resolver.Should().BeAssignableTo<IVaultPathResolver>();
        resolver.VaultRoot.Should().Be(_testVaultDir);
    }

    [Theory]
    [InlineData(@"C:\MyVault", @"C:\MyVault\staging\sample.m4a", "staging/sample.m4a")]
    [InlineData(@"C:\MyVault", @"C:\MyVault\routed\transcripts\meeting.md", "routed/transcripts/meeting.md")]
    [InlineData("/MyVault", "/MyVault/staging/sample.m4a", "staging/sample.m4a")]
    [InlineData("/MyVault", "/MyVault/.ledger/recordings/rec_123.json", ".ledger/recordings/rec_123.json")]
    [InlineData("/MyVault", "staging/sample.m4a", "staging/sample.m4a")]
    [InlineData(@"C:\MyVault", @"staging\sample.m4a", "staging/sample.m4a")]
    public void NormalizeToVaultRelative_WhenPathIsWithinVault_ReturnsPosixRelativePath(
        string vaultRoot,
        string inputPath,
        string expectedRelativePath)
    {
        // Arrange
        var resolver = new VaultPathResolver(vaultRoot);

        // Act
        var actual = resolver.NormalizeToVaultRelative(inputPath);

        // Assert
        actual.Should().Be(expectedRelativePath);
    }

    [Theory]
    [InlineData(@"C:\MyVault", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\MyVault", @"D:\OtherVault\staging\sample.m4a")]
    [InlineData("/MyVault", "/etc/passwd")]
    [InlineData("/MyVault", "/var/log/syslog")]
    [InlineData("/MyVault", "../outside.txt")]
    [InlineData(@"C:\MyVault", @"..\outside.txt")]
    public void NormalizeToVaultRelative_WhenPathIsOutsideVault_ThrowsArgumentException(
        string vaultRoot,
        string outsidePath)
    {
        // Arrange
        var resolver = new VaultPathResolver(vaultRoot);

        // Act
        var act = () => resolver.NormalizeToVaultRelative(outsidePath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("test:file*name?with\"bad<chars>pipe|end", "test_file_name_with_bad_chars_pipe_end")]
    [InlineData("normal-clean_filename.123", "normal-clean_filename.123")]
    [InlineData("title with spaces and (parentheses)", "title with spaces and (parentheses)")]
    public void SanitizePathSegment_WhenSegmentContainsIllegalCharacters_ReplacesOrStripsIllegalCharacters(
        string inputSegment,
        string expectedSanitized)
    {
        // Arrange
        var resolver = new VaultPathResolver(_testVaultDir);

        // Act
        var sanitized = resolver.SanitizePathSegment(inputSegment);

        // Assert
        sanitized.Should().Be(expectedSanitized);
        sanitized.Should().NotContainAny(":", "*", "?", "\"", "<", ">", "|", "/", "\\");
    }

    [Fact]
    public void ResolvePlatformPath_WhenGivenPosixRelativePath_CombinesWithVaultRootUsingPlatformSeparator()
    {
        // Arrange
        var resolver = new VaultPathResolver(_testVaultDir);
        var relativePath = "staging/sample.m4a";
        var expectedPath = Path.Combine(_testVaultDir, "staging", "sample.m4a");

        // Act
        var resolved = resolver.ResolvePlatformPath(relativePath);

        // Assert
        resolved.Should().Be(expectedPath);
    }

    [Fact]
    public void EnsureVaultDirectories_WhenCalled_CreatesExpectedIntermediateDirectories()
    {
        // Arrange
        var resolver = new VaultPathResolver(_testVaultDir);

        // Act
        resolver.EnsureVaultDirectories();

        // Assert
        var expectedRelativeFolders = new[]
        {
            "staging",
            Path.Combine(".ledger", "recordings"),
            Path.Combine(".ledger", "speakers"),
            Path.Combine(".ledger", "leases"),
            Path.Combine(".ledger", "locks"),
            Path.Combine(".ledger", "templates"),
            "routed",
            "_needs_review"
        };

        foreach (var relativeFolder in expectedRelativeFolders)
        {
            var absoluteFolder = Path.Combine(_testVaultDir, relativeFolder);
            Directory.Exists(absoluteFolder).Should().BeTrue($"directory '{relativeFolder}' should have been created");
        }
    }
}
