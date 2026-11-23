using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Inspection and enablement of Windows audit policies required for USB detection.
/// </summary>
public interface IAuditPolicyService
{
    /// <summary>
    /// Returns whether Audit PNP Activity and Audit Removable Storage are enabled.
    /// Does not modify system policy.
    /// </summary>
    Task<AuditSettingsStatus> GetAuditSettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks required audit settings and enables any that are disabled, then re-queries.
    /// Used on USB/removable plug-in so file-level detection can proceed.
    /// </summary>
    Task<AuditSettingsStatus> EnsureRequiredAuditSettingsAsync(CancellationToken cancellationToken);
}
