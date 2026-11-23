namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Host identity context attached to every outbound ingest payload.
/// </summary>
public sealed class HostInfo
{
    public string HostName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string LoggedInUser { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
}
