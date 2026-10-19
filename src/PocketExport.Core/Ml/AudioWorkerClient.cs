using System.Net.Http.Json;
using System.Text.Json;

namespace PocketExport.Core.Ml;

/// <summary>
///     HTTP IPC client for communicating with the external Python audio worker.
/// </summary>
public sealed class AudioWorkerClient : IAudioWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioWorkerClient"/> class.
    /// </summary>
    /// <param name="httpClient">Preconfigured HTTP client pointing to the worker service base URL.</param>
    public AudioWorkerClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);

        var requestPayload = new TranscribeRequest(audioPath);
        using var response = await _httpClient.PostAsJsonAsync("/transcribe", requestPayload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranscriptionResult>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Audio worker returned an empty transcription response.");
    }
}
