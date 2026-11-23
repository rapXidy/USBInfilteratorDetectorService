namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// In-memory USB session being reconstructed before final enqueue.
/// </summary>
public sealed class UsbSessionState
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string DeviceKey { get; set; } = string.Empty;
    public string DeviceSerial { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
    public long TotalBytesWritten { get; set; }
    public bool AbruptRemoval { get; set; }
    public bool AuditChecked { get; set; }
    public AuditSettingsStatus AuditSettings { get; set; } = new();
    public List<UsbEventRecord> Events { get; set; } = new();
    public HashSet<long> SeenRecordIds { get; set; } = new();
    public DateTimeOffset? CooldownUntil { get; set; }
    public bool Finalized { get; set; }
}
