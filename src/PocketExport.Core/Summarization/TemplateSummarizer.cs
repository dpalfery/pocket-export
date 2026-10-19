using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;

namespace PocketExport.Core.Summarization;

/// <summary>
/// Generates markdown summaries for transcripts by loading templates from the vault ledger
/// and querying a local LLM chat completions endpoint.
/// </summary>
public sealed class TemplateSummarizer : ITemplateSummarizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IVaultPathResolver _pathResolver;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateSummarizer"/> class.
    /// </summary>
    /// <param name="pathResolver">The vault path resolver service.</param>
    /// <param name="httpClient">The HTTP client configured for local LLM completion requests.</param>
    public TemplateSummarizer(IVaultPathResolver pathResolver, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(httpClient);
        _pathResolver = pathResolver;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<string> GenerateSummaryAsync(
        string? templateId,
        string transcript,
        IReadOnlyList<string>? speakers,
        ClassificationResult classification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(classification);

        string templateContent = await LoadTemplateContentAsync(templateId, cancellationToken);
        string renderedPrompt = RenderPrompt(templateContent, transcript, speakers, classification);

        var requestPayload = new
        {
            model = "local-model",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are an AI assistant generating structured summaries of audio recordings based on templates."
                },
                new
                {
                    role = "user",
                    content = renderedPrompt
                }
            },
            temperature = 0.3
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload, SerializerOptions);
        using var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var requestUri = _httpClient.BaseAddress is not null
            ? new Uri("/v1/chat/completions", UriKind.Relative)
            : new Uri("http://localhost:1234/v1/chat/completions");

        using var response = await _httpClient.PostAsync(requestUri, requestContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractSummaryContent(responseJson);
    }

    private async Task<string> LoadTemplateContentAsync(string? templateId, CancellationToken cancellationToken)
    {
        string? requestedPath = null;
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            string relativePath = $".ledger/templates/{templateId.Trim()}.md";
            string platformPath = _pathResolver.ResolvePlatformPath(relativePath);
            if (File.Exists(platformPath))
            {
                return await File.ReadAllTextAsync(platformPath, cancellationToken);
            }

            requestedPath = platformPath;
        }

        string defaultRelativePath = ".ledger/templates/default.md";
        string defaultPlatformPath = _pathResolver.ResolvePlatformPath(defaultRelativePath);
        if (File.Exists(defaultPlatformPath))
        {
            return await File.ReadAllTextAsync(defaultPlatformPath, cancellationToken);
        }

        string missingDetail = requestedPath is not null
            ? $"Template '{templateId}' was not found at '{requestedPath}', and fallback default template was not found at '{defaultPlatformPath}'."
            : $"Default template was not found at '{defaultPlatformPath}'.";

        throw new FileNotFoundException(missingDetail, requestedPath ?? defaultPlatformPath);
    }

    private static string RenderPrompt(
        string templateContent,
        string transcript,
        IReadOnlyList<string>? speakers,
        ClassificationResult classification)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert AI assistant tasked with generating structured markdown summaries of audio transcripts.");
        sb.AppendLine();
        sb.AppendLine("### Classification Metadata");
        sb.AppendLine($"- Domain: {classification.Domain}");
        sb.AppendLine($"- Customer: {classification.Customer}");
        sb.AppendLine($"- Project: {classification.Project}");
        sb.AppendLine($"- Initiative: {classification.Initiative}");
        sb.AppendLine($"- Confidence: {classification.Confidence}");
        sb.AppendLine();
        sb.AppendLine("### Identified Speakers");
        if (speakers != null && speakers.Count > 0)
        {
            foreach (string speaker in speakers)
            {
                sb.AppendLine($"- {speaker}");
            }
        }
        else
        {
            sb.AppendLine("- None");
        }
        sb.AppendLine();
        sb.AppendLine("### Template Instructions");
        sb.AppendLine(templateContent);
        sb.AppendLine();
        sb.AppendLine("### Transcript");
        sb.AppendLine(transcript);

        return sb.ToString();
    }

    private static string ExtractSummaryContent(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
