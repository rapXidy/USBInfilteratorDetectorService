namespace USBInfilteratorDetectorService.Configuration;

/// <summary>
/// Strongly typed binding for the AppSettings configuration section.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    /// Organisation / tenant identifier included on every outbound payload.
    /// </summary>
    public string CorpCode { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the central ingest API (HTTPS).
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret sent as the X-Api-Key header. Leave empty in committed settings.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Directory used for the SQLite offline queue and allowed-device cache.
    /// </summary>
    public string OfflineQueuePath { get; set; } = @"C:\ProgramData\UsbDetector\queue";

    /// <summary>
    /// Documented polling cadence for USB presence checks (host loop is cron-driven).
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of queue items sent in a single ingest batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// When true, emits additional diagnostic logging.
    /// </summary>
    public bool EnableVerboseLogging { get; set; }

    /// <summary>
    /// Cronos expression controlling how often the detection job runs (UTC).
    /// </summary>
    public string CronInterval { get; set; } = "*/1 * * * *";

    /// <summary>
    /// Seconds to keep monitoring event logs after a USB device is removed.
    /// </summary>
    public int UsbCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Directory for Serilog rolling file logs.
    /// </summary>
    public string LogPath { get; set; } = @"C:\ProgramData\UsbDetector\logs";

    /// <summary>
    /// Optional Seq server URL. When empty, the Seq sink is not enabled.
    /// </summary>
    public string SeqServerUrl { get; set; } = string.Empty;
}
