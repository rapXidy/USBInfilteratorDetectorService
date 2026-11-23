namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// CamelCase-serialisable ingest payload matching Docs/02 §3.
/// </summary>
public sealed class UsbSessionPayload
{
    public string CorpCode { get; set; } = string.Empty;
    public HostInfo Host { get; set; } = new();
    public UsbSessionDetail Session { get; set; } = new();
    public List<UsbEventRecord> Events { get; set; } = new();
    public AuditSettingsStatus AuditSettingsStatus { get; set; } = new();
}

/// <summary>
/// Session block inside an ingest payload.
/// </summary>
public sealed class UsbSessionDetail
{
    public Guid SessionId { get; set; }
    public string DeviceSerial { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
    public long TotalBytesWritten { get; set; }
    public int RiskScore { get; set; }
    public List<RiskReason> Reasons { get; set; } = new();
}
