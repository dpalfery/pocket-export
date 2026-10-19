using PocketExport.Core.Storage;

namespace PocketExport.Api.Services;

/// <summary>
/// Represents a discrete byte slice within an audio container with calculated range bounds.
/// </summary>
/// <param name="StartByte">The starting byte offset (zero-based, inclusive).</param>
/// <param name="EndByte">The ending byte offset (zero-based, inclusive).</param>
/// <param name="TotalBytes">The total size in bytes of the audio file.</param>
public sealed record AudioByteRange(
    long StartByte,
    long EndByte,
    long TotalBytes)
{
    /// <summary>
    /// Gets the total byte count represented by this byte range slice.
    /// </summary>
    public long Length => EndByte - StartByte + 1;
}

/// <summary>
/// Service contract for slicing and streaming audio byte ranges over HTTP responses.
/// </summary>
public interface IAudioSnippetStreamer
{
    /// <summary>
    /// Streams an audio byte slice from disk to the target destination stream.
    /// </summary>
    /// <param name="filePath">The absolute platform path to the source audio file.</param>
    /// <param name="startByte">The starting byte offset (inclusive).</param>
    /// <param name="endByte">The ending byte offset (inclusive).</param>
    /// <param name="destination">The destination stream to write the audio slice to.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous streaming operation.</param>
    /// <returns>A task that completes when all bytes in the specified range are written.</returns>
    Task StreamSnippetToAsync(
        string filePath,
        long startByte,
        long endByte,
        Stream destination,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compatibility and convenience extension methods for <see cref="IVaultPathResolver"/>.
/// </summary>
public static class VaultPathResolverExtensions
{
    /// <summary>
    /// Resolves an intra-vault relative path to an absolute platform path.
    /// </summary>
    /// <param name="resolver">The vault path resolver instance.</param>
    /// <param name="relativePath">The relative path within the vault.</param>
    /// <returns>The combined absolute platform path.</returns>
    public static string ResolveAbsolutePath(this IVaultPathResolver resolver, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return resolver.ResolvePlatformPath(relativePath);
    }
}
