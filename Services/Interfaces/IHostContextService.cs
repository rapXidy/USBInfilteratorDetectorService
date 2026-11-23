using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Resolves local host identity for outbound payloads.
/// </summary>
public interface IHostContextService
{
    /// <summary>
    /// Builds the current host context (name, machine GUID, logged-on user, OS version).
    /// </summary>
    HostInfo GetHostInfo();
}
