using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// HTTPS client that posts batched payloads to POST /api/ingest/batch.
/// </summary>
public sealed class IngestApiClient : IIngestApiClient
{
    private readonly HttpClient _httpClient;
    private readonly WorkerOptions _options;
    private readonly ILogger<IngestApiClient> _logger;

    /// <summary>
    /// Creates an ingest API client.
    /// </summary>
    public IngestApiClient(
        HttpClient httpClient,
        IOptions<WorkerOptions> options,
        ILogger<IngestApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// POSTs a JSON batch to /api/ingest/batch with the configured X-Api-Key header.
    /// </summary>
    public async Task<bool> SendBatchAsync(
        IReadOnlyList<string> payloadJsonItems,
        CancellationToken cancellationToken)
    {
        if (payloadJsonItems.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl is not configured; skipping ingest send");
            return false;
        }

        // Build a JSON array from already-serialised payload objects.
        var batchJson = "[" + string.Join(",", payloadJsonItems) + "]";
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ingest/batch")
        {
            Content = new StringContent(batchJson, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
        }
        else
        {
            _logger.LogWarning("ApiKey is empty; ingest request will likely be rejected");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Ingest batch accepted ({Count} payload(s), status {Status})",
                    payloadJsonItems.Count,
                    (int)response.StatusCode);
                return true;
            }

            if ((int)response.StatusCode is 401 or 403)
            {
                _logger.LogError(
                    "Ingest authentication failed with status {Status}",
                    (int)response.StatusCode);
            }
            else
            {
                _logger.LogWarning(
                    "Ingest batch rejected with status {Status}",
                    (int)response.StatusCode);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ingest batch send failed");
            return false;
        }
    }
}
