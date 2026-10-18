using System.Text.Json.Serialization;

namespace PocketExport.Core.Models;

/// <summary>
///     Boundaries in milliseconds for a solo speaker audio snippet.
/// </summary>
public sealed record SoloSnippetBounds(
    [property: JsonPropertyName("start_ms")]
    long StartMs,
    [property: JsonPropertyName("end_ms")] long EndMs);

/// <summary>
///     A detected speaker cluster in a recording and its biometric match against enrolled profiles.
/// </summary>
public sealed record RecordingSpeaker(
    [property: JsonPropertyName("cluster_id")]
    string ClusterId,
    [property: JsonPropertyName("enrolled_speaker_id")]
    string? EnrolledSpeakerId = null,
    [property: JsonPropertyName("display_name")]
    string? DisplayName = null,
    [property: JsonPropertyName("match_confidence")]
    double MatchConfidence = 0.0,
    [property: JsonPropertyName("needs_disambiguation")]
    bool NeedsDisambiguation = false,
    [property: JsonPropertyName("solo_snippet")]
    SoloSnippetBounds? SoloSnippet = null,
    [property: JsonPropertyName("embedding_vector")]
    IReadOnlyList<float>? EmbeddingVector = null);

/// <summary>
///     Result of classifying a recording transcript into a taxonomy hierarchy.
/// </summary>
public sealed record ClassificationResult(
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("customer")]
    string Customer,
    [property: JsonPropertyName("project")]
    string Project,
    [property: JsonPropertyName("initiative")]
    string Initiative,
    [property: JsonPropertyName("confidence")]
    double Confidence,
    [property: JsonPropertyName("template_id")]
    string TemplateId,
    [property: JsonPropertyName("rationale")]
    string? Rationale = null);

/// <summary>
///     Core recording manifest recording processing status, paths, speakers, and classification.
/// </summary>
public sealed record RecordingManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pocket_id")]
    string PocketId,
    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("duration_seconds")]
    double DurationSeconds,
    [property: JsonPropertyName("staging_audio_path")]
    string StagingAudioPath,
    [property: JsonPropertyName("pocket_metadata_path")]
    string? PocketMetadataPath = null,
    [property: JsonPropertyName("routed_transcript_path")]
    string? RoutedTranscriptPath = null,
    [property: JsonPropertyName("routed_summary_path")]
    string? RoutedSummaryPath = null,
    [property: JsonPropertyName("status")] string Status = "pending",
    [property: JsonPropertyName("error_message")]
    string? ErrorMessage = null,
    [property: JsonPropertyName("speakers")]
    IReadOnlyList<RecordingSpeaker>? Speakers = null,
    [property: JsonPropertyName("classification")]
    ClassificationResult? Classification = null);

/// <summary>
///     Enrolled biometric speaker profile stored in the voicebank ledger.
/// </summary>
public sealed record SpeakerProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")]
    string DisplayName,
    [property: JsonPropertyName("organization")]
    string Organization,
    [property: JsonPropertyName("enrolled_at")]
    DateTimeOffset EnrolledAt,
    [property: JsonPropertyName("source_recording_id")]
    string SourceRecordingId,
    [property: JsonPropertyName("sample_snippet_bounds")]
    SoloSnippetBounds SampleSnippetBounds,
    [property: JsonPropertyName("embedding")]
    IReadOnlyList<float> Embedding);

/// <summary>
///     Coordination lease manifest for distributed leader election across instances.
/// </summary>
public sealed record LeaseManifest(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("holder_id")]
    string HolderId,
    [property: JsonPropertyName("acquired_at")]
    DateTimeOffset AcquiredAt,
    [property: JsonPropertyName("expires_at")]
    DateTimeOffset ExpiresAt);

/// <summary>
///     A domain entry inside the taxonomy hierarchy.
/// </summary>
public sealed record TaxonomyDomain(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("customers")]
    IReadOnlyList<string>? Customers = null,
    [property: JsonPropertyName("projects")]
    IReadOnlyList<string>? Projects = null,
    [property: JsonPropertyName("initiatives")]
    IReadOnlyList<string>? Initiatives = null);

/// <summary>
///     Taxonomy configuration governing recording routing and classification.
/// </summary>
public sealed record TaxonomyConfig(
    [property: JsonPropertyName("domains")]
    IReadOnlyList<TaxonomyDomain> Domains,
    [property: JsonPropertyName("metadata")]
    IReadOnlyDictionary<string, string>? Metadata = null);