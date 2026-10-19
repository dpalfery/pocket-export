using System.Net;
using System.Text;
using FluentAssertions;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using PocketExport.Core.Taxonomy;
using Xunit;

namespace PocketExport.Core.Tests.Taxonomy;

/// <summary>
///     Unit and contract tests specifying taxonomy loading and LLM classification behavior.
/// </summary>
public sealed class TaxonomyClassifierTests : IDisposable
{
    private readonly string _testVaultDir;
    private readonly IVaultPathResolver _vaultPathResolver;

    public TaxonomyClassifierTests()
    {
        _testVaultDir = Path.Combine(Path.GetTempPath(), "pocket-export-taxonomy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testVaultDir);
        _vaultPathResolver = new VaultPathResolver(_testVaultDir);
        _vaultPathResolver.EnsureVaultDirectories();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultDir))
        {
            try
            {
                Directory.Delete(_testVaultDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task LoadTaxonomyAsync_WhenValidYamlExists_ParsesWorkAndPersonalDomainsWithCustomerAndProjectHierarchies()
    {
        // Arrange
        const string taxonomyYaml = """
            domains:
              - name: Work
                customers:
                  - Acme Corp
                  - Globex
                projects:
                  - Project Apollo
                  - Project Titan
                initiatives:
                  - Cloud Migration
              - name: Personal
                customers: []
                projects:
                  - Home Renovation
                  - Fitness
                initiatives:
                  - Health 2026
            metadata:
              version: "1.0.0"
              environment: "test"
            """;

        var rootYamlPath = _vaultPathResolver.ResolvePlatformPath("taxonomy.yaml");
        var ledgerYamlPath = _vaultPathResolver.ResolvePlatformPath(".ledger/taxonomy.yaml");
        await File.WriteAllTextAsync(rootYamlPath, taxonomyYaml, Encoding.UTF8);
        await File.WriteAllTextAsync(ledgerYamlPath, taxonomyYaml, Encoding.UTF8);

        using var httpClient = new HttpClient(new MockHttpMessageHandler("{}"))
        {
            BaseAddress = new Uri("http://localhost:1234/v1/")
        };

        ITaxonomyClassifier sut = new TaxonomyClassifier(_vaultPathResolver, httpClient);

        // Act
        var result = await sut.LoadTaxonomyAsync();

        // Assert
        result.Should().NotBeNull();
        result.Domains.Should().HaveCount(2);

        var workDomain = result.Domains.FirstOrDefault(d => d.Name == "Work");
        workDomain.Should().NotBeNull();
        workDomain!.Customers.Should().Contain(new[] { "Acme Corp", "Globex" });
        workDomain.Projects.Should().Contain(new[] { "Project Apollo", "Project Titan" });
        workDomain.Initiatives.Should().Contain("Cloud Migration");

        var personalDomain = result.Domains.FirstOrDefault(d => d.Name == "Personal");
        personalDomain.Should().NotBeNull();
        personalDomain!.Projects.Should().Contain(new[] { "Home Renovation", "Fitness" });
        personalDomain.Initiatives.Should().Contain("Health 2026");
    }

    [Fact]
    public async Task LoadTaxonomyAsync_WhenYamlFileMissing_ThrowsFileNotFoundException()
    {
        // Arrange
        using var httpClient = new HttpClient(new MockHttpMessageHandler("{}"))
        {
            BaseAddress = new Uri("http://localhost:1234/v1/")
        };

        ITaxonomyClassifier sut = new TaxonomyClassifier(_vaultPathResolver, httpClient);

        // Act
        var act = () => sut.LoadTaxonomyAsync();

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ClassifyRecordingAsync_WithValidTranscriptAndSpeakers_ConstructsStructuredPromptAndDeserializesLmStudioResponse()
    {
        // Arrange
        const string taxonomyYaml = """
            domains:
              - name: Work
                customers:
                  - Acme Corp
                projects:
                  - Project Apollo
                initiatives:
                  - Cloud Migration
            """;

        var rootYamlPath = _vaultPathResolver.ResolvePlatformPath("taxonomy.yaml");
        var ledgerYamlPath = _vaultPathResolver.ResolvePlatformPath(".ledger/taxonomy.yaml");
        await File.WriteAllTextAsync(rootYamlPath, taxonomyYaml, Encoding.UTF8);
        await File.WriteAllTextAsync(ledgerYamlPath, taxonomyYaml, Encoding.UTF8);

        const string lmStudioResponseJson = """
            {
              "id": "chatcmpl-test-001",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "{\"domain\": \"Work\", \"customer\": \"Acme Corp\", \"project\": \"Project Apollo\", \"initiative\": \"Cloud Migration\", \"confidence\": 0.95, \"template_id\": \"meeting-notes\", \"rationale\": \"Sprint sync discussing Acme Corp cloud migration on Project Apollo.\"}"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        var mockHandler = new MockHttpMessageHandler(lmStudioResponseJson);
        using var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1/")
        };

        ITaxonomyClassifier sut = new TaxonomyClassifier(_vaultPathResolver, httpClient);
        await sut.LoadTaxonomyAsync();

        const string transcript = "In this meeting with Alice and Bob, we reviewed the Acme Corp cloud migration roadmap for Project Apollo.";
        var speakers = new List<string> { "Alice", "Bob" };
        const string pocketSummary = "Review of Acme Corp cloud migration schedule and architecture.";

        // Act
        var result = await sut.ClassifyRecordingAsync(transcript, speakers, pocketSummary);

        // Assert
        result.Should().NotBeNull();
        result.Domain.Should().Be("Work");
        result.Customer.Should().Be("Acme Corp");
        result.Project.Should().Be("Project Apollo");
        result.Initiative.Should().Be("Cloud Migration");
        result.Confidence.Should().BeApproximately(0.95, 0.001);
        result.TemplateId.Should().Be("meeting-notes");
        result.Rationale.Should().Be("Sprint sync discussing Acme Corp cloud migration on Project Apollo.");

        mockHandler.LastRequest.Should().NotBeNull();
        mockHandler.LastRequestBody.Should().NotBeNullOrWhiteSpace();
        mockHandler.LastRequestBody.Should().Contain("Acme Corp");
        mockHandler.LastRequestBody.Should().Contain("Project Apollo");
        mockHandler.LastRequestBody.Should().Contain("Alice");
        mockHandler.LastRequestBody.Should().Contain("Bob");
        mockHandler.LastRequestBody.Should().Contain("Review of Acme Corp cloud migration schedule and architecture.");
    }

    [Fact]
    public async Task ClassifyRecordingAsync_WhenLmStudioReturnsMalformedJson_ThrowsException()
    {
        // Arrange
        const string invalidResponseJson = """
            {
              "id": "chatcmpl-test-err",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Unable to classify recording"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        var mockHandler = new MockHttpMessageHandler(invalidResponseJson);
        using var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1/")
        };

        ITaxonomyClassifier sut = new TaxonomyClassifier(_vaultPathResolver, httpClient);

        // Act
        var act = () => sut.ClassifyRecordingAsync("Transcript text", ["Speaker1"], "Summary text");

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
