using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Detects currently connected USB removable storage devices.
/// </summary>
public interface IUsbPresenceService
{
    /// <summary>
    /// Queries WMI for USB disk drives and maps attached volumes.
    /// </summary>
    Task<IReadOnlyList<UsbDeviceInfo>> GetConnectedUsbDevicesAsync(CancellationToken cancellationToken);
}
