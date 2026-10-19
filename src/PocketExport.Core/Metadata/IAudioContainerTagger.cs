namespace PocketExport.Core.Metadata;

/// <summary>
/// Provides audio container metadata tagging for MP3 (ID3) and M4A/MP4 audio files.
/// </summary>
public interface IAudioContainerTagger
{
    /// <summary>
    /// Tags the specified audio file with metadata without altering the underlying audio stream.
    /// </summary>
    /// <param name="audioPath">The platform path to the audio file.</param>
    /// <param name="title">The title tag value.</param>
    /// <param name="artists">The list of performer/artist names.</param>
    /// <param name="album">The album tag value.</param>
    /// <param name="comment">The comment tag value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when tagging is finished.</returns>
    Task TagAudioFileAsync(
        string audioPath,
        string title,
        IReadOnlyList<string> artists,
        string album,
        string comment,
        CancellationToken cancellationToken = default);
}
