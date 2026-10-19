namespace PocketExport.Core.Pocket;

using System.Text.Json.Serialization;

/// <summary>
/// Summary representation of a recording returned by the Pocket API.
/// </summary>
public sealed record PocketRecordingSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("duration_seconds")] double DurationSeconds);

/// <summary>
/// Defines the HTTP client contract for communicating with the HeyPocket API.
/// </summary>
public interface IPocketApiClient
{
    /// <summary>
    /// Retrieves a list of recording summaries for the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of recording summaries.</returns>
    Task<IReadOnlyList<PocketRecordingSummary>> ListRecordingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtains a pre-signed audio download URL for the specified recording identifier.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A pre-signed <see cref="Uri"/> pointing to the audio resource.</returns>
    Task<Uri> GetAudioUrlAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the detailed recording endpoint including transcripts and summarizations.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The recording detail response string.</returns>
    Task<string> GetRecordingDetailAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves verbatim raw metadata JSON for a recording from the HeyPocket cloud API.
    /// </summary>
    /// <param name="id">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw JSON string payload returned by the server.</returns>
    Task<string> GetRawRecordingDetailJsonAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams audio content from a pre-signed URI.
    /// </summary>
    /// <param name="audioUri">The pre-signed URI pointing to the audio file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A readable <see cref="Stream"/> of the audio content.</returns>
    Task<Stream> DownloadAudioStreamAsync(Uri audioUri, CancellationToken cancellationToken = default);
}
