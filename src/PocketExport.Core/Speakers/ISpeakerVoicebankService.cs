namespace PocketExport.Core.Speakers;

using PocketExport.Core.Models;

/// <summary>
/// Biometric speaker identification result.
/// </summary>
/// <param name="SpeakerId">The enrolled speaker identifier if identified; otherwise null.</param>
/// <param name="DisplayName">The display name of the speaker, or a cluster label (e.g. SPEAKER_00) if unidentified.</param>
/// <param name="Confidence">Cosine similarity confidence score.</param>
/// <param name="NeedsDisambiguation">Indicates whether the confidence score falls into the disambiguation range (0.70 - 0.74).</param>
public sealed record SpeakerMatchResult(
    string? SpeakerId,
    string DisplayName,
    double Confidence,
    bool NeedsDisambiguation);

/// <summary>
/// Service contract for speaker enrollment, biometric matching against voicebank profiles, and retroactive scanning.
/// </summary>
public interface ISpeakerVoicebankService
{
    /// <summary>
    /// Matches a speaker biometric embedding vector against enrolled profiles in the voicebank.
    /// </summary>
    /// <param name="embedding">The 512-d biometric embedding vector.</param>
    /// <param name="cloudSpeakerHints">Optional list of cloud speaker name hints from recording metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match result including speaker id, display name, confidence, and disambiguation status.</returns>
    Task<SpeakerMatchResult> MatchSpeakerAsync(
        float[] embedding,
        IReadOnlyList<string>? cloudSpeakerHints = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a new speaker profile into the ledger and SQLite index, and scans historical recordings for retroactive matches.
    /// </summary>
    /// <param name="name">The speaker's display name.</param>
    /// <param name="organization">The speaker's organization.</param>
    /// <param name="embedding">The speaker's biometric embedding vector.</param>
    /// <param name="recordingId">The source recording identifier where the snippet was extracted.</param>
    /// <param name="bounds">The start and end bounds of the solo speech snippet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly enrolled speaker profile.</returns>
    Task<SpeakerProfile> EnrollSpeakerAsync(
        string name,
        string organization,
        float[] embedding,
        string recordingId,
        SoloSnippetBounds bounds,
        CancellationToken cancellationToken = default);
}
