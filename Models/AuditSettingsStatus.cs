namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Snapshot of required Windows audit policies for USB detection completeness.
/// </summary>
public sealed class AuditSettingsStatus
{
    public bool PnpActivityEnabled { get; set; }
    public bool RemovableStorageEnabled { get; set; }
    public bool DetectionComplete { get; set; }
}
