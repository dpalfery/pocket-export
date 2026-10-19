namespace PocketExport.Core.Pocket;

using System.Net.Http.Headers;
using System.Text.Json;

/// <summary>
/// HTTP client implementation for interacting with the HeyPocket public REST API.
/// </summary>
public sealed class PocketApiClient : IPocketApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _bearerToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="PocketApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The underlying HTTP client.</param>
    /// <param name="bearerToken">Optional Bearer token for authenticating API requests.</param>
    public PocketApiClient(HttpClient httpClient, string? bearerToken = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _bearerToken = bearerToken;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PocketRecordingSummary>> ListRecordingsAsync(CancellationToken cancellationToken = default)
    {
        var uri = CreateRequestUri("public/recordings");
        using var request = CreateRequest(HttpMethod.Get, uri);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<PocketRecordingSummary>();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var items = JsonSerializer.Deserialize<List<PocketRecordingSummary>>(json, JsonOptions);
            return items ?? (IReadOnlyList<PocketRecordingSummary>)Array.Empty<PocketRecordingSummary>();
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (doc.RootElement.TryGetProperty("recordings", out var recProp))
            {
                var items = JsonSerializer.Deserialize<List<PocketRecordingSummary>>(recProp.GetRawText(), JsonOptions);
                return items ?? (IReadOnlyList<PocketRecordingSummary>)Array.Empty<PocketRecordingSummary>();
            }

            if (doc.RootElement.TryGetProperty("data", out var dataProp))
            {
                var items = JsonSerializer.Deserialize<List<PocketRecordingSummary>>(dataProp.GetRawText(), JsonOptions);
                return items ?? (IReadOnlyList<PocketRecordingSummary>)Array.Empty<PocketRecordingSummary>();
            }
        }

        return Array.Empty<PocketRecordingSummary>();
    }

    /// <inheritdoc />
    public async Task<Uri> GetAudioUrlAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var uri = CreateRequestUri($"public/recordings/{Uri.EscapeDataString(id)}/audio-url");
        using var request = CreateRequest(HttpMethod.Get, uri);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? audioUrlString = null;
        if (root.TryGetProperty("audio_url", out var audioUrlProp) ||
            root.TryGetProperty("audioUrl", out audioUrlProp) ||
            root.TryGetProperty("url", out audioUrlProp))
        {
            audioUrlString = audioUrlProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(audioUrlString))
        {
            throw new InvalidOperationException($"Audio URL response for recording '{id}' did not contain a valid 'audio_url'.");
        }

        return new Uri(audioUrlString);
    }

    /// <inheritdoc />
    public async Task<string> GetRecordingDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var uri = CreateRequestUri($"public/recordings/{Uri.EscapeDataString(id)}?include_transcript=true&include_summarizations=true");
        using var request = CreateRequest(HttpMethod.Get, uri);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetRawRecordingDetailJsonAsync(string id, CancellationToken cancellationToken = default)
    {
        return GetRecordingDetailAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAudioStreamAsync(Uri audioUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioUri);

        var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, audioUri),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        return request;
    }

    private Uri CreateRequestUri(string relativePath)
    {
        if (_httpClient.BaseAddress == null)
        {
            return new Uri($"https://public.heypocketai.com/api/v1/{relativePath.TrimStart('/')}");
        }

        var baseAddressStr = _httpClient.BaseAddress.AbsoluteUri;
        if (!baseAddressStr.EndsWith('/'))
        {
            baseAddressStr += "/";
        }

        return new Uri(new Uri(baseAddressStr), relativePath.TrimStart('/'));
    }
}
