using System.Text.Json.Serialization;
using PocketExport.Core.Models;

namespace PocketExport.Api.Models;

/// <summary>
/// Request payload to update the taxonomy classification and template routing for an existing recording.
/// </summary>
/// <param name="Domain">The primary domain classification (e.g. Engineering, Sales, Product).</param>
/// <param name="Customer">The customer or client organization name.</param>
/// <param name="Project">The associated project or workstream identifier.</param>
/// <param name="Initiative">The strategic initiative or quarter tag.</param>
/// <param name="TemplateId">Optional markdown summary template identifier override.</param>
public sealed record RetagRecordingRequest(
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("customer")] string Customer,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("initiative")] string Initiative,
    [property: JsonPropertyName("template_id")] string? TemplateId = null);

/// <summary>
/// Request payload to enroll a new biometric speaker profile from a solo audio speech segment.
/// </summary>
/// <param name="Name">The display name of the speaker.</param>
/// <param name="Organization">The organizational affiliation of the speaker.</param>
/// <param name="Embedding">The 512-dimensional biometric embedding vector.</param>
/// <param name="RecordingId">The identifier of the source recording where the solo snippet was identified.</param>
/// <param name="Bounds">The start and end millisecond timestamps of the solo snippet.</param>
public sealed record EnrollSpeakerRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("organization")] string Organization,
    [property: JsonPropertyName("embedding")] float[] Embedding,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("bounds")] SoloSnippetBounds Bounds);

/// <summary>
/// Request payload to batch approve proposed retroactive speaker matches.
/// </summary>
/// <param name="MatchIds">The list of unique proposed retroactive match record identifiers to approve.</param>
public sealed record ApproveRetroactiveMatchesRequest(
    [property: JsonPropertyName("match_ids")] IReadOnlyList<long> MatchIds);
