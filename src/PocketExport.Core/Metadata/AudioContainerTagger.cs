namespace PocketExport.Core.Metadata;

/// <summary>
/// Default implementation of <see cref="IAudioContainerTagger"/> using TagLibSharp.
/// </summary>
public sealed class AudioContainerTagger : IAudioContainerTagger
{
    /// <inheritdoc />
    public Task TagAudioFileAsync(
        string audioPath,
        string title,
        IReadOnlyList<string> artists,
        string album,
        string comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(artists);
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(comment);

        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException($"Audio file not found at '{audioPath}'.", audioPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var tagFile = TagLib.File.Create(audioPath);
        tagFile.Tag.Title = title;
        tagFile.Tag.Performers = artists.ToArray();
        tagFile.Tag.Album = album;
        tagFile.Tag.Comment = comment;
        tagFile.Save();

        return Task.CompletedTask;
    }
}
