using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Durable SQLite offline queue for ingest payloads.
/// </summary>
public interface IOfflineQueueStore
{
    /// <summary>
    /// Ensures the queue database and schema exist.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a payload JSON row before any network send.
    /// </summary>
    Task EnqueueAsync(string payloadJson, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the next undelivered items in FIFO order, respecting back-off.
    /// </summary>
    Task<IReadOnlyList<QueueItem>> DequeuePendingAsync(int maxBatchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a queue item as successfully delivered.
    /// </summary>
    Task MarkDeliveredAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed delivery attempt and schedules the next retry with exponential back-off.
    /// </summary>
    Task MarkFailedAsync(long id, int attemptCount, CancellationToken cancellationToken);
}
