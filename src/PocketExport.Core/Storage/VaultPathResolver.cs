using System.Text;

namespace PocketExport.Core.Storage;

/// <summary>
/// Default implementation of <see cref="IVaultPathResolver"/> that validates and normalizes vault paths.
/// </summary>
public sealed class VaultPathResolver : IVaultPathResolver
{
    private static readonly char[] IllegalSegmentChars = [':', '*', '?', '"', '<', '>', '|', '/', '\\'];

    private static readonly string[] RequiredDirectories =
    [
        "staging",
        ".ledger/recordings",
        ".ledger/speakers",
        ".ledger/leases",
        ".ledger/locks",
        ".ledger/templates",
        "routed",
        "_needs_review"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultPathResolver"/> class.
    /// </summary>
    /// <param name="vaultRoot">The absolute path to the vault root.</param>
    public VaultPathResolver(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        VaultRoot = vaultRoot;
    }

    /// <inheritdoc />
    public string VaultRoot { get; }

    /// <inheritdoc />
    public string NormalizeToVaultRelative(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var (inputRoot, inputSegments) = ParsePath(path);
        var (vaultRootPrefix, vaultSegments) = ParsePath(VaultRoot);

        if (inputRoot is null)
        {
            return string.Join('/', inputSegments);
        }

        bool isWindows = vaultRootPrefix is not null && vaultRootPrefix.EndsWith(":/", StringComparison.Ordinal);
        StringComparison comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(inputRoot, vaultRootPrefix, comparison))
        {
            throw new ArgumentException($"Path '{path}' is not located within vault root '{VaultRoot}'.", nameof(path));
        }

        if (inputSegments.Count < vaultSegments.Count)
        {
            throw new ArgumentException($"Path '{path}' is outside vault root '{VaultRoot}'.", nameof(path));
        }

        for (int i = 0; i < vaultSegments.Count; i++)
        {
            if (!string.Equals(inputSegments[i], vaultSegments[i], comparison))
            {
                throw new ArgumentException($"Path '{path}' is not located within vault root '{VaultRoot}'.", nameof(path));
            }
        }

        return string.Join('/', inputSegments.Skip(vaultSegments.Count));
    }

    /// <inheritdoc />
    public string SanitizePathSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var trimmed = segment.Trim();
        if (trimmed == "." || trimmed == ".." || trimmed.All(c => c == '.'))
        {
            throw new ArgumentException($"Path segment cannot be a directory traversal token: '{segment}'.", nameof(segment));
        }

        var sb = new StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            if (Array.IndexOf(IllegalSegmentChars, c) >= 0 || char.IsControl(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        var resultTrimmed = result.Trim();
        if (resultTrimmed == "." || resultTrimmed == ".." || resultTrimmed.All(c => c == '.'))
        {
            throw new ArgumentException($"Path segment cannot be a directory traversal token: '{segment}'.", nameof(segment));
        }

        return result;
    }

    /// <inheritdoc />
    public string ResolvePlatformPath(string posixRelativePath)
    {
        ArgumentNullException.ThrowIfNull(posixRelativePath);

        string normalized = posixRelativePath.Replace('\\', '/').Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string canonicalVaultRoot = Path.GetFullPath(VaultRoot);
        string canonicalCombined = parts.Length == 0
            ? canonicalVaultRoot
            : Path.GetFullPath(Path.Combine([canonicalVaultRoot, .. parts]));

        string prefix = canonicalVaultRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalVaultRoot
            : canonicalVaultRoot + Path.DirectorySeparatorChar;

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!canonicalCombined.Equals(canonicalVaultRoot, comparison) &&
            !canonicalCombined.StartsWith(prefix, comparison))
        {
            throw new ArgumentException($"Resolved path '{canonicalCombined}' is outside vault root '{VaultRoot}'.", nameof(posixRelativePath));
        }

        return canonicalCombined;
    }

    /// <inheritdoc />
    public void EnsureVaultDirectories()
    {
        Directory.CreateDirectory(VaultRoot);
        foreach (string relativeDir in RequiredDirectories)
        {
            string absoluteDir = ResolvePlatformPath(relativeDir);
            Directory.CreateDirectory(absoluteDir);
        }
    }

    private static (string? Root, List<string> Segments) ParsePath(string path)
    {
        string normalized = path.Replace('\\', '/');

        string? root = null;
        string remainder;

        if (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
        {
            if (normalized.Length == 2)
            {
                root = normalized.ToUpperInvariant() + "/";
                remainder = string.Empty;
            }
            else if (normalized[2] == '/')
            {
                root = normalized[..2].ToUpperInvariant() + "/";
                remainder = normalized[3..];
            }
            else
            {
                root = normalized[..2].ToUpperInvariant() + "/";
                remainder = normalized[2..];
            }
        }
        else if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            int nextSlash = normalized.IndexOf('/', 2);
            if (nextSlash >= 0)
            {
                int shareSlash = normalized.IndexOf('/', nextSlash + 1);
                if (shareSlash >= 0)
                {
                    root = normalized[..(shareSlash + 1)];
                    remainder = normalized[(shareSlash + 1)..];
                }
                else
                {
                    root = normalized + "/";
                    remainder = string.Empty;
                }
            }
            else
            {
                root = normalized + "/";
                remainder = string.Empty;
            }
        }
        else if (normalized.StartsWith('/'))
        {
            root = "/";
            remainder = normalized.TrimStart('/');
        }
        else
        {
            root = null;
            remainder = normalized;
        }

        string[] rawSegments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<string>();

        foreach (string seg in rawSegments)
        {
            if (seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException($"Path '{path}' attempts directory traversal above its root.", nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(seg);
            }
        }

        return (root, segments);
    }
}
