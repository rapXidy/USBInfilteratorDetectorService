using System.Text.Json;
using Microsoft.Extensions.Options;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Explainable, rule-based risk scoring engine with capped totals.
/// </summary>
public sealed class RiskScoringEngine : IRiskScoringEngine
{
    private const int MaxRiskScore = 100;
    private const long LargeVolumeThresholdBytes = 500L * 1024 * 1024; // 500 MB

    private readonly WorkerOptions _options;
    private readonly ILogger<RiskScoringEngine> _logger;
    private HashSet<string>? _allowedSerials;

    /// <summary>
    /// Creates a risk scoring engine.
    /// </summary>
    public RiskScoringEngine(IOptions<WorkerOptions> options, ILogger<RiskScoringEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates risk rules against a session and returns the capped score plus reasons.
    /// </summary>
    public (int RiskScore, IReadOnlyList<RiskReason> Reasons) Score(UsbSessionState session)
    {
        var reasons = new List<RiskReason>();

        EvaluateLargeVolumeWrite(session, reasons);
        EvaluateUnapprovedDevice(session, reasons);
        EvaluateAfterHours(session, reasons);
        EvaluateAbruptRemoval(session, reasons);
        EvaluateAuditSettingsDisabled(session, reasons);

        var score = Math.Min(MaxRiskScore, reasons.Sum(r => r.Weight));
        return (score, reasons);
    }

    /// <summary>
    /// Fires when a large volume of data was written to the removable device.
    /// </summary>
    private static void EvaluateLargeVolumeWrite(UsbSessionState session, List<RiskReason> reasons)
    {
        if (session.TotalBytesWritten < LargeVolumeThresholdBytes)
        {
            return;
        }

        var gb = session.TotalBytesWritten / (1024d * 1024d * 1024d);
        reasons.Add(new RiskReason
        {
            Rule = "LargeVolumeWrite",
            Weight = 30,
            Description = $"{gb:0.0} GB written to removable volume",
            EventRecordIds = session.Events
                .Where(e => e.EventId is 4663 or 4656)
                .Select(e => e.RecordId)
                .Distinct()
                .ToList()
        });
    }

    /// <summary>
    /// Fires when the device serial is absent from the local Allowed Devices cache.
    /// </summary>
    private void EvaluateUnapprovedDevice(UsbSessionState session, List<RiskReason> reasons)
    {
        var allowed = GetAllowedSerials();
        if (allowed.Count == 0)
        {
            // Empty cache means the organisation has not provisioned a list yet; still flag unknowns.
        }

        if (!string.IsNullOrWhiteSpace(session.DeviceSerial) &&
            allowed.Contains(session.DeviceSerial))
        {
            return;
        }

        reasons.Add(new RiskReason
        {
            Rule = "UnapprovedDevice",
            Weight = 25,
            Description = "Device serial not in Allowed Devices list",
            EventRecordIds = session.Events
                .Where(e => e.EventId == 6416)
                .Select(e => e.RecordId)
                .Distinct()
                .DefaultIfEmpty(0)
                .Where(id => id != 0)
                .ToList()
        });
    }

    /// <summary>
    /// Fires when USB activity occurs outside typical working hours (08:00–18:00 local).
    /// </summary>
    private static void EvaluateAfterHours(UsbSessionState session, List<RiskReason> reasons)
    {
        var local = session.ConnectedAt.ToLocalTime();
        var isWeekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var outsideHours = local.Hour < 8 || local.Hour >= 18;

        if (!isWeekend && !outsideHours)
        {
            return;
        }

        reasons.Add(new RiskReason
        {
            Rule = "AfterHoursActivity",
            Weight = 15,
            Description = $"USB activity outside normal working hours ({local:HH:mm} local)",
            EventRecordIds = session.Events.Select(e => e.RecordId).Distinct().Take(10).ToList()
        });
    }

    /// <summary>
    /// Fires when the device was removed abruptly without a clean eject signal.
    /// </summary>
    private static void EvaluateAbruptRemoval(UsbSessionState session, List<RiskReason> reasons)
    {
        if (!session.AbruptRemoval || session.DisconnectedAt is null)
        {
            return;
        }

        reasons.Add(new RiskReason
        {
            Rule = "AbruptRemoval",
            Weight = 20,
            Description = "Device removed abruptly without clean eject",
            EventRecordIds = session.Events
                .Where(e => e.Activity is "PnPLifecycle" or "DeviceConnected")
                .Select(e => e.RecordId)
                .Distinct()
                .Take(5)
                .ToList()
        });
    }

    /// <summary>
    /// Fires when required audit settings are disabled, marking detection incomplete.
    /// </summary>
    private static void EvaluateAuditSettingsDisabled(UsbSessionState session, List<RiskReason> reasons)
    {
        if (session.AuditSettings.DetectionComplete)
        {
            return;
        }

        reasons.Add(new RiskReason
        {
            Rule = "AuditSettingsDisabled",
            Weight = 40,
            Description = "Required audit settings are disabled – file-level detection incomplete",
            EventRecordIds = new List<long>()
        });
    }

    /// <summary>
    /// Loads allowed device serials from a local cache file under the offline queue path.
    /// </summary>
    private HashSet<string> GetAllowedSerials()
    {
        if (_allowedSerials is not null)
        {
            return _allowedSerials;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(_options.OfflineQueuePath, "allowed-devices.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                foreach (var serial in items.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    set.Add(serial.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load allowed-devices cache");
        }

        _allowedSerials = set;
        return set;
    }
}
