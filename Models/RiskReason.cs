namespace USBInfilteratorDetectorService.Models;

/// <summary>
/// Explainable risk rule result citing the event record IDs that triggered it.
/// </summary>
public sealed class RiskReason
{
    public string Rule { get; set; } = string.Empty;
    public int Weight { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<long> EventRecordIds { get; set; } = new();
}
