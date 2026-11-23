using System.Diagnostics.Eventing.Reader;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;
using WinEventLogReader = System.Diagnostics.Eventing.Reader.EventLogReader;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Reads Security and PnP-related Event Log channels for USB activity.
/// </summary>
public sealed class EventLogReader : IEventLogReader
{
    private static readonly int[] TargetEventIds = { 6416, 4663, 4656 };

    private readonly ILogger<EventLogReader> _logger;

    /// <summary>
    /// Creates an event log reader.
    /// </summary>
    public EventLogReader(ILogger<EventLogReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reads new USB-related events since the given watermark, tolerating missing channels.
    /// </summary>
    public Task<IReadOnlyList<UsbEventRecord>> ReadUsbEventsAsync(
        DateTimeOffset sinceUtc,
        IReadOnlyCollection<string>? volumeDriveLetters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<UsbEventRecord>();

        ReadSecurityChannel(sinceUtc, volumeDriveLetters, results, cancellationToken);
        ReadOptionalChannel(
            "Microsoft-Windows-Kernel-PnP/Configuration",
            sinceUtc,
            results,
            cancellationToken);
        ReadOptionalChannel(
            "Microsoft-Windows-DriverFrameworks-UserMode/Operational",
            sinceUtc,
            results,
            cancellationToken);

        return Task.FromResult<IReadOnlyList<UsbEventRecord>>(
            results.OrderBy(e => e.TimeCreated).ToList());
    }

    /// <summary>
    /// Reads the Security log for device recognition and object-access events.
    /// </summary>
    private void ReadSecurityChannel(
        DateTimeOffset sinceUtc,
        IReadOnlyCollection<string>? volumeDriveLetters,
        List<UsbEventRecord> results,
        CancellationToken cancellationToken)
    {
        var query =
            $"*[System[(EventID=6416 or EventID=4663 or EventID=4656) and TimeCreated[@SystemTime>='{sinceUtc.UtcDateTime:O}']]]";

        try
        {
            var logQuery = new EventLogQuery("Security", PathType.LogName, query);
            using var reader = new WinEventLogReader(logQuery);

            for (EventRecord? record = reader.ReadEvent();
                 record is not null;
                 record = reader.ReadEvent())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (record)
                {
                    if (!TargetEventIds.Contains(record.Id))
                    {
                        continue;
                    }

                    var mapped = MapSecurityEvent(record, volumeDriveLetters);
                    if (mapped is not null)
                    {
                        results.Add(mapped);
                    }
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogDebug("Security event log not available on this host");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Insufficient privilege to read the Security event log");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading Security event log");
        }
    }

    /// <summary>
    /// Best-effort read of an optional diagnostic / PnP channel.
    /// </summary>
    private void ReadOptionalChannel(
        string channelName,
        DateTimeOffset sinceUtc,
        List<UsbEventRecord> results,
        CancellationToken cancellationToken)
    {
        var query = $"*[System[TimeCreated[@SystemTime>='{sinceUtc.UtcDateTime:O}']]]";

        try
        {
            var logQuery = new EventLogQuery(channelName, PathType.LogName, query);
            using var reader = new WinEventLogReader(logQuery);

            var count = 0;
            for (EventRecord? record = reader.ReadEvent();
                 record is not null && count < 200;
                 record = reader.ReadEvent(), count++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (record)
                {
                    results.Add(new UsbEventRecord
                    {
                        EventId = record.Id,
                        RecordId = record.RecordId ?? 0,
                        TimeCreated = record.TimeCreated?.ToUniversalTime() ?? DateTimeOffset.UtcNow,
                        Activity = "PnPLifecycle",
                        Description = SafeFormat(record)
                    });
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogDebug("Optional event channel {Channel} is not present", channelName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping optional event channel {Channel}", channelName);
        }
    }

    /// <summary>
    /// Maps a Security EventRecord into a normalised USB event, filtering object access by volume when possible.
    /// </summary>
    private static UsbEventRecord? MapSecurityEvent(
        EventRecord record,
        IReadOnlyCollection<string>? volumeDriveLetters)
    {
        var description = SafeFormat(record);
        var activity = record.Id switch
        {
            6416 => "DeviceConnected",
            4663 => "ObjectAccess",
            4656 => "HandleRequest",
            _ => "Unknown"
        };

        if (record.Id is 4663 or 4656 &&
            volumeDriveLetters is { Count: > 0 } &&
            description.Contains(":\\", StringComparison.Ordinal) &&
            !volumeDriveLetters.Any(letter =>
                description.Contains(letter.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        long? bytesWritten = null;
        if (record.Id == 4663 &&
            (description.Contains("WriteData", StringComparison.OrdinalIgnoreCase) ||
             description.Contains("AppendData", StringComparison.OrdinalIgnoreCase)))
        {
            bytesWritten = EstimateBytesFromDescription(description);
        }

        return new UsbEventRecord
        {
            EventId = record.Id,
            RecordId = record.RecordId ?? 0,
            TimeCreated = record.TimeCreated?.ToUniversalTime() ?? DateTimeOffset.UtcNow,
            Activity = activity,
            Description = Truncate(description, 1024),
            BytesWritten = bytesWritten
        };
    }

    /// <summary>
    /// Attempts a coarse byte estimate from an event description when TransferSize is present.
    /// </summary>
    private static long? EstimateBytesFromDescription(string description)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            description,
            @"(?:TransferSize|Length|bytes)\s*[:=]?\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && long.TryParse(match.Groups[1].Value, out var bytes) && bytes > 0)
        {
            return bytes;
        }

        return 4096;
    }

    /// <summary>
    /// Safely formats an event record description.
    /// </summary>
    private static string SafeFormat(EventRecord record)
    {
        try
        {
            return record.FormatDescription() ?? $"Event {record.Id}";
        }
        catch
        {
            return $"Event {record.Id}";
        }
    }

    /// <summary>
    /// Truncates long event descriptions for payload size control.
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
