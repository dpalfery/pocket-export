namespace PocketExport.Core.Storage;

/// <summary>
/// Provides path resolution, normalization, and validation services for files stored within the vault.
/// </summary>
public interface IVaultPathResolver
{
    /// <summary>
    /// Gets the absolute platform path to the root of the vault.
    /// </summary>
    string VaultRoot { get; }

    /// <summary>
    /// Converts a platform or relative path to a POSIX relative path originating from <see cref="VaultRoot"/>.
    /// </summary>
    /// <param name="path">The absolute or relative path to normalize.</param>
    /// <returns>A POSIX relative path with forward slashes.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is outside the vault or attempts directory traversal.</exception>
    string NormalizeToVaultRelative(string path);

    /// <summary>
    /// Sanitizes an individual path segment by replacing illegal cross-platform characters with safe characters.
    /// </summary>
    /// <param name="segment">The path segment to sanitize.</param>
    /// <returns>The sanitized path segment.</returns>
    string SanitizePathSegment(string segment);

    /// <summary>
    /// Combines <see cref="VaultRoot"/> with the specified POSIX relative path using the platform directory separator.
    /// </summary>
    /// <param name="posixRelativePath">The POSIX relative path within the vault.</param>
    /// <returns>The combined platform path.</returns>
    string ResolvePlatformPath(string posixRelativePath);

    /// <summary>
    /// Ensures that standard vault directories exist on disk.
    /// </summary>
    void EnsureVaultDirectories();
}
