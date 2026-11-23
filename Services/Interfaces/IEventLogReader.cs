using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Reads Windows Event Log channels relevant to USB activity.
/// </summary>
public interface IEventLogReader
{
    /// <summary>
    /// Reads new USB-related events since the given watermark, tolerating missing channels.
    /// </summary>
    Task<IReadOnlyList<UsbEventRecord>> ReadUsbEventsAsync(
        DateTimeOffset sinceUtc,
        IReadOnlyCollection<string>? volumeDriveLetters,
        CancellationToken cancellationToken);
}
