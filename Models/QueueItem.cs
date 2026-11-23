namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Durable offline-queue row awaiting FIFO delivery to the ingest API.
/// </summary>
public sealed class QueueItem
{
    public long Id { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptUtc { get; set; }
    public bool Delivered { get; set; }
}
