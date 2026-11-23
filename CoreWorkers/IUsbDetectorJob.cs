namespace USBInfilteratorDetectorService.CoreWorkers;

/// <summary>
/// Orchestrates one USB detection / scoring / enqueue / drain tick.
/// </summary>
public interface IUsbDetectorJob
{
    /// <summary>
    /// Runs a full detection cycle for the current cron occurrence.
    /// </summary>
    Task Run(CancellationToken cancellationToken);
}
