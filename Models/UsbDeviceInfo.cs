namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Physical USB removable storage device discovered via WMI.
/// </summary>
public sealed class UsbDeviceInfo
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DeviceSerial { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? PnpDeviceId { get; set; }
    public IReadOnlyList<string> VolumeDriveLetters { get; set; } = Array.Empty<string>();
}
