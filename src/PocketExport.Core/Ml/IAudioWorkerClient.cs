using System.Text.Json.Serialization;
using PocketExport.Core.Models;

namespace PocketExport.Core.Ml;

/// <summary>
///     A timed transcription segment with speaker attribution.
/// </summary>
public sealed record TranscriptionSegment
{
    /// <summary>
    ///     Gets the segment start timestamp in milliseconds.
    /// </summary>
    [JsonPropertyName("start_ms")]
    public long StartMs { get; init; }

    /// <summary>
    ///     Gets the segment end timestamp in milliseconds.
    /// </summary>
    [JsonPropertyName("end_ms")]
    public long EndMs { get; init; }

    /// <summary>
    ///     Gets the transcribed text of the segment.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the speaker cluster identifier (e.g. SPEAKER_00).
    /// </summary>
    [JsonPropertyName("speaker")]
    public string Speaker { get; init; } = string.Empty;
}

/// <summary>
///     Result of an audio transcription request containing full text, diarized segments, and speaker biometric clusters.
/// </summary>
public sealed record TranscriptionResult
{
    /// <summary>
    ///     Gets the full transcribed text.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the diarized transcription segments.
    /// </summary>
    [JsonPropertyName("segments")]
    public IReadOnlyList<TranscriptionSegment> Segments { get; init; } = [];

    /// <summary>
    ///     Gets the extracted speaker clusters with 512-d biometric embeddings and solo snippets.
    /// </summary>
    [JsonPropertyName("speakers")]
    public IReadOnlyList<RecordingSpeaker> Speakers { get; init; } = [];
}

/// <summary>
///     Payload dispatched to the audio worker /transcribe endpoint.
/// </summary>
public sealed record TranscribeRequest(
    [property: JsonPropertyName("audio_path")]
    string AudioPath);

/// <summary>
///     Client for interacting with the audio worker HTTP API.
/// </summary>
public interface IAudioWorkerClient
{
    /// <summary>
    ///     Submits an audio file path to the audio worker for transcription and diarization.
    /// </summary>
    /// <param name="audioPath">Vault-relative or local audio path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Strongly typed transcription result.</returns>
    Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default);
}
