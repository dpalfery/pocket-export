using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PocketExport.Core.Taxonomy;

/// <summary>
/// Default implementation of <see cref="ITaxonomyClassifier"/> that parses YAML taxonomy definitions
/// and queries LM Studio for transcript classification.
/// </summary>
public sealed class TaxonomyClassifier : ITaxonomyClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly HttpClient _httpClient;
    private TaxonomyConfig? _taxonomyConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaxonomyClassifier"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">Resolver for platform and vault-relative paths.</param>
    /// <param name="httpClient">HTTP client configured to communicate with LM Studio.</param>
    public TaxonomyClassifier(IVaultPathResolver vaultPathResolver, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(vaultPathResolver);
        ArgumentNullException.ThrowIfNull(httpClient);

        _vaultPathResolver = vaultPathResolver;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<TaxonomyConfig> LoadTaxonomyAsync(CancellationToken cancellationToken = default)
    {
        string ledgerPath = _vaultPathResolver.ResolvePlatformPath(".ledger/taxonomy.yaml");
        string rootPath = _vaultPathResolver.ResolvePlatformPath("taxonomy.yaml");

        string resolvedPath;
        if (File.Exists(ledgerPath))
        {
            resolvedPath = ledgerPath;
        }
        else if (File.Exists(rootPath))
        {
            resolvedPath = rootPath;
        }
        else
        {
            throw new FileNotFoundException(
                $"Taxonomy YAML file was not found at '{ledgerPath}' or '{rootPath}'.",
                rootPath);
        }

        string yamlContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var dto = deserializer.Deserialize<TaxonomyYamlDto>(yamlContent);
        if (dto == null)
        {
            throw new InvalidOperationException($"Failed to deserialize taxonomy configuration from '{resolvedPath}'.");
        }

        var domains = dto.Domains?.Select(d => new TaxonomyDomain(
            Name: d.Name ?? string.Empty,
            Customers: d.Customers != null ? d.Customers.AsReadOnly() : null,
            Projects: d.Projects != null ? d.Projects.AsReadOnly() : null,
            Initiatives: d.Initiatives != null ? d.Initiatives.AsReadOnly() : null
        )).ToList() ?? new List<TaxonomyDomain>();

        _taxonomyConfig = new TaxonomyConfig(
            Domains: domains.AsReadOnly(),
            Metadata: dto.Metadata
        );

        return _taxonomyConfig;
    }

    /// <inheritdoc />
    public async Task<ClassificationResult> ClassifyRecordingAsync(
        string transcript,
        IReadOnlyList<string> speakers,
        string pocketSummary,
        CancellationToken cancellationToken = default)
    {
        if (_taxonomyConfig == null)
        {
            try
            {
                await LoadTaxonomyAsync(cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // Proceed if taxonomy file is not present in vault
            }
        }

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an expert taxonomy classification assistant.");
        promptBuilder.AppendLine("Classify the provided recording transcript and metadata into the taxonomy hierarchy.");
        promptBuilder.AppendLine();

        if (_taxonomyConfig?.Domains != null && _taxonomyConfig.Domains.Count > 0)
        {
            promptBuilder.AppendLine("Taxonomy Hierarchy:");
            foreach (var domain in _taxonomyConfig.Domains)
            {
                promptBuilder.AppendLine($"- Domain: {domain.Name}");
                if (domain.Customers != null && domain.Customers.Count > 0)
                {
                    promptBuilder.AppendLine($"  Customers: {string.Join(", ", domain.Customers)}");
                }
                if (domain.Projects != null && domain.Projects.Count > 0)
                {
                    promptBuilder.AppendLine($"  Projects: {string.Join(", ", domain.Projects)}");
                }
                if (domain.Initiatives != null && domain.Initiatives.Count > 0)
                {
                    promptBuilder.AppendLine($"  Initiatives: {string.Join(", ", domain.Initiatives)}");
                }
            }
            promptBuilder.AppendLine();
        }

        if (speakers != null && speakers.Count > 0)
        {
            promptBuilder.AppendLine($"Speakers: {string.Join(", ", speakers)}");
        }

        if (!string.IsNullOrWhiteSpace(pocketSummary))
        {
            promptBuilder.AppendLine($"Summary: {pocketSummary}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Transcript:");
        promptBuilder.AppendLine(transcript);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Respond strictly with a JSON object adhering to this schema:");
        promptBuilder.AppendLine("""
            {
              "domain": "<Domain Name>",
              "customer": "<Customer Name>",
              "project": "<Project Name>",
              "initiative": "<Initiative Name>",
              "confidence": 0.95,
              "template_id": "<Template Id>",
              "rationale": "<Reasoning for classification>"
            }
            """);

        var requestPayload = new
        {
            model = "local-model",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a taxonomy classification assistant. Respond strictly with valid JSON."
                },
                new
                {
                    role = "user",
                    content = promptBuilder.ToString()
                }
            },
            temperature = 0.1
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var requestUri = _httpClient.BaseAddress == null
            ? new Uri("http://localhost:1234/v1/chat/completions")
            : new Uri("chat/completions", UriKind.Relative);

        var response = await _httpClient.PostAsync(requestUri, requestContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("LM Studio response missing choices array.");
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var messageElement) ||
            !messageElement.TryGetProperty("content", out var contentElement))
        {
            throw new InvalidOperationException("LM Studio response choice missing message content.");
        }

        var rawContent = contentElement.GetString();
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidOperationException("LM Studio returned empty content.");
        }

        string cleanJson = rawContent.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson[7..];
        }
        else if (cleanJson.StartsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[3..];
        }

        if (cleanJson.EndsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[..^3];
        }
        cleanJson = cleanJson.Trim();

        var classification = JsonSerializer.Deserialize<ClassificationResult>(cleanJson, JsonOptions);
        if (classification == null)
        {
            throw new InvalidOperationException("Failed to deserialize classification result from model content.");
        }

        return classification;
    }

    private sealed class TaxonomyYamlDto
    {
        public List<TaxonomyDomainYamlDto>? Domains { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    private sealed class TaxonomyDomainYamlDto
    {
        public string? Name { get; set; }
        public List<string>? Customers { get; set; }
        public List<string>? Projects { get; set; }
        public List<string>? Initiatives { get; set; }
    }
}
