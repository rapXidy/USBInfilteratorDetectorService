namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Normalised Windows Event Log entry associated with a USB session.
/// </summary>
public sealed class UsbEventRecord
{
    public int EventId { get; set; }
    public long RecordId { get; set; }
    public DateTimeOffset TimeCreated { get; set; }
    public string Activity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long? BytesWritten { get; set; }
}
