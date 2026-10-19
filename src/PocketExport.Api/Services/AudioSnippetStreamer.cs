using PocketExport.Core.Ledger;
using PocketExport.Core.Storage;

namespace PocketExport.Api.Services;

/// <summary>
/// Streams discreet byte slices from audio container files for solo speaker snippets.
/// </summary>
public sealed class AudioSnippetStreamer : IAudioSnippetStreamer
{
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioSnippetStreamer"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">The vault path resolver instance.</param>
    /// <param name="ledgerManager">The ledger manager instance.</param>
    public AudioSnippetStreamer(IVaultPathResolver vaultPathResolver, ILedgerManager ledgerManager)
    {
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
    }

    /// <summary>
    /// Calculates the byte offset slice for an audio snippet given the audio container size, total duration, and segment timestamps.
    /// </summary>
    /// <param name="totalBytes">The total size in bytes of the audio container file.</param>
    /// <param name="durationSeconds">The total duration in seconds of the audio recording.</param>
    /// <param name="startMs">The starting offset in milliseconds.</param>
    /// <param name="endMs">The ending offset in milliseconds.</param>
    /// <returns>A calculated <see cref="AudioByteRange"/> indicating start, end, and total byte lengths.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="startMs"/> is negative or greater than <paramref name="endMs"/>, or when totals are non-positive.
    /// </exception>
    public static AudioByteRange CalculateByteRange(long totalBytes, double durationSeconds, long startMs, long endMs)
    {
        if (startMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startMs), startMs, "startMs cannot be negative.");
        }

        if (startMs > endMs)
        {
            throw new ArgumentOutOfRangeException(nameof(startMs), startMs, "startMs must be less than or equal to endMs.");
        }

        if (totalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), totalBytes, "totalBytes must be positive.");
        }

        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "durationSeconds must be positive.");
        }

        double bytesPerMs = totalBytes / (durationSeconds * 1000.0);
        long startByte = (long)Math.Floor(startMs * bytesPerMs);
        long endByte = (long)Math.Floor(endMs * bytesPerMs);

        if (startByte >= totalBytes)
        {
            startByte = totalBytes - 1;
        }

        if (endByte >= totalBytes)
        {
            endByte = totalBytes - 1;
        }

        return new AudioByteRange(startByte, endByte, totalBytes);
    }

    /// <inheritdoc />
    public async Task StreamSnippetToAsync(
        string filePath,
        long startByte,
        long endByte,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(destination);

        if (startByte < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startByte), startByte, "startByte cannot be negative.");
        }

        if (endByte < startByte)
        {
            throw new ArgumentOutOfRangeException(nameof(endByte), endByte, "endByte cannot be less than startByte.");
        }

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        fileStream.Seek(startByte, SeekOrigin.Begin);

        long bytesToRead = endByte - startByte + 1;
        int initialBufferSize = (int)Math.Min(64 * 1024, bytesToRead);
        byte[] buffer = new byte[initialBufferSize];

        while (bytesToRead > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, bytesToRead);
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesToRead -= bytesRead;
        }
    }
}
