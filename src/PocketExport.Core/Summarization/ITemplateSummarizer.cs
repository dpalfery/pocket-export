using PocketExport.Core.Models;

namespace PocketExport.Core.Summarization;

/// <summary>
/// Defines the contract for generating structured markdown summaries from transcripts and templates using local LLMs.
/// </summary>
public interface ITemplateSummarizer
{
    /// <summary>
    /// Generates a structured markdown summary for a recording transcript using the specified template and classification metadata.
    /// </summary>
    /// <param name="templateId">The template identifier. If empty or not found, falls back to the default template.</param>
    /// <param name="transcript">The full dialogue transcript text.</param>
    /// <param name="speakers">The list of identified speaker names or labels.</param>
    /// <param name="classification">The classification result containing domain, customer, project, and confidence.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>The generated markdown summary text.</returns>
    Task<string> GenerateSummaryAsync(
        string? templateId,
        string transcript,
        IReadOnlyList<string>? speakers,
        ClassificationResult classification,
        CancellationToken cancellationToken = default);
}
