using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Read-only inspection of Windows audit policies required for USB detection.
/// </summary>
public interface IAuditPolicyService
{
    /// <summary>
    /// Returns whether Audit PNP Activity and Audit Removable Storage are enabled.
    /// Never modifies system policy.
    /// </summary>
    Task<AuditSettingsStatus> GetAuditSettingsAsync(CancellationToken cancellationToken);
}
