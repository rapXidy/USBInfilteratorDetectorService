using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Applies explainable weighted rules to produce a capped risk score.
/// </summary>
public interface IRiskScoringEngine
{
    /// <summary>
    /// Evaluates risk rules against a session and returns the capped score plus reasons.
    /// </summary>
    (int RiskScore, IReadOnlyList<RiskReason> Reasons) Score(UsbSessionState session);
}
