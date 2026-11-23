namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// HTTPS client for posting batched USB session payloads to the central API.
/// </summary>
public interface IIngestApiClient
{
    /// <summary>
    /// POSTs a JSON batch to /api/ingest/batch with the configured X-Api-Key header.
    /// </summary>
    Task<bool> SendBatchAsync(IReadOnlyList<string> payloadJsonItems, CancellationToken cancellationToken);
}
