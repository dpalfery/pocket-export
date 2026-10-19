using System.Net;
using System.Text;
using FluentAssertions;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;
using PocketExport.Core.Summarization;
using Xunit;

namespace PocketExport.Core.Tests.Summarization;

/// <summary>
/// Unit tests specifying the behavioral contract for template loading and LM Studio summarization.
/// </summary>
public sealed class TemplateSummarizerTests : IDisposable
{
    private readonly string _vaultRoot;
    private readonly IVaultPathResolver _pathResolver;

    public TemplateSummarizerTests()
    {
        _vaultRoot = Path.Combine(Path.GetTempPath(), "pocket-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultRoot);
        _pathResolver = new VaultPathResolver(_vaultRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultRoot))
        {
            try
            {
                Directory.Delete(_vaultRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task GenerateSummaryAsync_WhenTemplateExists_LoadsTemplateAndOutputsFormattedMarkdownSummary()
    {
        // Arrange
        var templatesDir = Path.Combine(_vaultRoot, ".ledger", "templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "technical-review.md"),
            "# Technical Review Template\nFocus on decisions and next steps.");

        const string expectedMarkdown = "# Technical Review Summary\n\n## Decisions\n- Use SQLite\n\n## Next Steps\n- Implement Router";
        var mockResponse = $$"""
            {
                "id": "chatcmpl-123",
                "object": "chat.completion",
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "{{expectedMarkdown.Replace("\n", "\\n")}}"
                        },
                        "finish_reason": "stop"
                    }
                ]
            }
            """;

        var handler = new MockHttpMessageHandler(mockResponse);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        ITemplateSummarizer summarizer = new TemplateSummarizer(_pathResolver, httpClient);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "Internal",
            Project: "PocketExport",
            Initiative: "SyncEngine",
            Confidence: 0.95,
            TemplateId: "technical-review");
        var speakers = new List<string> { "Alice", "Bob" };
        const string transcript = "Alice: Let's discuss SQLite caching. Bob: Agreed.";

        // Act
        var result = await summarizer.GenerateSummaryAsync("technical-review", transcript, speakers, classification);

        // Assert
        result.Should().Be(expectedMarkdown);
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("Technical Review Template");
        handler.LastRequestBody.Should().Contain("Alice: Let's discuss SQLite caching.");
        handler.LastRequestBody.Should().Contain("Alice");
        handler.LastRequestBody.Should().Contain("Bob");
        handler.LastRequestBody.Should().Contain("PocketExport");
    }

    [Fact]
    public async Task GenerateSummaryAsync_WhenTemplateDoesNotExist_FallsBackToDefaultTemplate()
    {
        // Arrange
        var templatesDir = Path.Combine(_vaultRoot, ".ledger", "templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "default.md"),
            "# Default Summary Template\nStandard meeting summary.");

        const string expectedMarkdown = "# Default Summary\n- Key points discussed.";
        var mockResponse = $$"""
            {
                "choices": [
                    {
                        "message": {
                            "role": "assistant",
                            "content": "{{expectedMarkdown.Replace("\n", "\\n")}}"
                        }
                    }
                ]
            }
            """;

        var handler = new MockHttpMessageHandler(mockResponse);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        ITemplateSummarizer summarizer = new TemplateSummarizer(_pathResolver, httpClient);
        var classification = new ClassificationResult(
            Domain: "General",
            Customer: "None",
            Project: "AdHoc",
            Initiative: "None",
            Confidence: 0.8,
            TemplateId: "missing-custom-template");
        var speakers = new List<string> { "Charlie" };
        const string transcript = "Charlie: Quick status update.";

        // Act
        var result = await summarizer.GenerateSummaryAsync("missing-custom-template", transcript, speakers, classification);

        // Assert
        result.Should().Be(expectedMarkdown);
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("Default Summary Template");
    }

    [Fact]
    public async Task GenerateSummaryAsync_WhenTemplateIdIsEmpty_FallsBackToDefaultTemplate()
    {
        // Arrange
        var templatesDir = Path.Combine(_vaultRoot, ".ledger", "templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "default.md"),
            "# Default Summary Template\nFallback when no template id is given.");

        const string expectedMarkdown = "# Fallback Summary";
        var mockResponse = $$"""
            {
                "choices": [
                    {
                        "message": {
                            "role": "assistant",
                            "content": "{{expectedMarkdown}}"
                        }
                    }
                ]
            }
            """;

        var handler = new MockHttpMessageHandler(mockResponse);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        ITemplateSummarizer summarizer = new TemplateSummarizer(_pathResolver, httpClient);
        var classification = new ClassificationResult(
            Domain: "General",
            Customer: "None",
            Project: "General",
            Initiative: "None",
            Confidence: 0.8,
            TemplateId: "");
        var speakers = new List<string> { "Charlie" };
        const string transcript = "Charlie: Testing empty template fallback.";

        // Act
        var result = await summarizer.GenerateSummaryAsync("", transcript, speakers, classification);

        // Assert
        result.Should().Be(expectedMarkdown);
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("Fallback when no template id is given.");
    }

    [Fact]
    public async Task GenerateSummaryAsync_WhenLmStudioReturnsHttpError_ThrowsHttpRequestException()
    {
        // Arrange
        var templatesDir = Path.Combine(_vaultRoot, ".ledger", "templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "default.md"),
            "# Default Template");

        var handler = new MockHttpMessageHandler("Internal Server Error", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        ITemplateSummarizer summarizer = new TemplateSummarizer(_pathResolver, httpClient);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "Internal",
            Project: "Core",
            Initiative: "Sync",
            Confidence: 0.85,
            TemplateId: "default");
        var speakers = new List<string> { "Dave" };

        // Act
        var act = () => summarizer.GenerateSummaryAsync("default", "Some transcript text", speakers, classification);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GenerateSummaryAsync_WhenNeitherRequestedNorDefaultTemplateExists_ThrowsFileNotFoundException()
    {
        // Arrange: empty templates dir
        var handler = new MockHttpMessageHandler("{}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        ITemplateSummarizer summarizer = new TemplateSummarizer(_pathResolver, httpClient);
        var classification = new ClassificationResult(
            Domain: "Engineering",
            Customer: "Internal",
            Project: "Core",
            Initiative: "Sync",
            Confidence: 0.85,
            TemplateId: "nonexistent");
        var speakers = new List<string> { "Dave" };

        // Act
        var act = () => summarizer.GenerateSummaryAsync("nonexistent", "Some transcript text", speakers, classification);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
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
