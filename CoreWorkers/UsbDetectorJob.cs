using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.CoreWorkers;

/// <summary>
/// Scoped job that detects USB activity, scores sessions, enqueues payloads, and drains the offline queue.
/// </summary>
public sealed class UsbDetectorJob : IUsbDetectorJob
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly WorkerOptions _options;
    private readonly IHostContextService _hostContext;
    private readonly IUsbPresenceService _usbPresence;
    private readonly IAuditPolicyService _auditPolicy;
    private readonly IEventLogReader _eventLogReader;
    private readonly ISessionBuilder _sessionBuilder;
    private readonly IRiskScoringEngine _riskScoring;
    private readonly IOfflineQueueStore _queueStore;
    private readonly IIngestApiClient _ingestApiClient;
    private readonly ILogger<UsbDetectorJob> _logger;

    /// <summary>
    /// Creates the USB detector job with required collaborators.
    /// </summary>
    public UsbDetectorJob(
        IOptions<WorkerOptions> options,
        IHostContextService hostContext,
        IUsbPresenceService usbPresence,
        IAuditPolicyService auditPolicy,
        IEventLogReader eventLogReader,
        ISessionBuilder sessionBuilder,
        IRiskScoringEngine riskScoring,
        IOfflineQueueStore queueStore,
        IIngestApiClient ingestApiClient,
        ILogger<UsbDetectorJob> logger)
    {
        _options = options.Value;
        _hostContext = hostContext;
        _usbPresence = usbPresence;
        _auditPolicy = auditPolicy;
        _eventLogReader = eventLogReader;
        _sessionBuilder = sessionBuilder;
        _riskScoring = riskScoring;
        _queueStore = queueStore;
        _ingestApiClient = ingestApiClient;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full detection cycle: detect → audit/events → session/risk → enqueue → drain.
    /// </summary>
    public async Task Run(CancellationToken cancellationToken)
    {
        _logger.LogInformation("USB detector tick starting at {Time}", DateTimeOffset.UtcNow);

        await _queueStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var host = _hostContext.GetHostInfo();
        var connected = await _usbPresence.GetConnectedUsbDevicesAsync(cancellationToken).ConfigureAwait(false);
        var connectedKeys = new HashSet<string>(connected.Select(d => d.DeviceKey), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, _options.UsbCooldownSeconds));

        await UpdateSessionsAsync(connected, connectedKeys, now, cooldown, cancellationToken)
            .ConfigureAwait(false);

        await FinalizeClosedSessionsAsync(host, now, cancellationToken).ConfigureAwait(false);
        await DrainQueueAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("USB detector tick completed at {Time}", DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Opens new sessions, refreshes active ones, and starts cooldown for removed devices.
    /// </summary>
    private async Task UpdateSessionsAsync(
        IReadOnlyList<UsbDeviceInfo> connected,
        HashSet<string> connectedKeys,
        DateTimeOffset now,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        foreach (var device in connected)
        {
            var session = _sessionBuilder.FindSession(device.DeviceKey);
            if (session is null)
            {
                session = _sessionBuilder.OpenSession(device, now);
                _logger.LogInformation(
                    "USB session {SessionId} opened for serial {Serial}",
                    session.SessionId,
                    MaskSerial(session.DeviceSerial));
            }
            else if (session.CooldownUntil is not null)
            {
                // Device reappeared during cooldown — resume monitoring.
                session.CooldownUntil = null;
                session.DisconnectedAt = null;
                session.AbruptRemoval = false;
            }

            await EnrichSessionAsync(session, device, cancellationToken).ConfigureAwait(false);
        }

        foreach (var session in _sessionBuilder.GetActiveSessions())
        {
            if (connectedKeys.Contains(session.DeviceKey))
            {
                continue;
            }

            if (session.DisconnectedAt is null)
            {
                // Absence without a clean-eject event is treated as abrupt removal.
                _sessionBuilder.BeginCooldown(session, now, abruptRemoval: true, cooldown);
                _logger.LogInformation(
                    "USB session {SessionId} entered cooldown after removal",
                    session.SessionId);
            }

            if (session.CooldownUntil is not null && now <= session.CooldownUntil)
            {
                await EnrichSessionAsync(session, device: null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Checks audit policy (once), reads new events, and updates session volume/reasons inputs.
    /// </summary>
    private async Task EnrichSessionAsync(
        UsbSessionState session,
        UsbDeviceInfo? device,
        CancellationToken cancellationToken)
    {
        if (!session.AuditChecked)
        {
            session.AuditSettings = await _auditPolicy
                .GetAuditSettingsAsync(cancellationToken)
                .ConfigureAwait(false);
            session.AuditChecked = true;
        }

        var since = session.Events.Count == 0
            ? session.ConnectedAt.AddSeconds(-5)
            : session.Events.Max(e => e.TimeCreated);

        var driveLetters = device?.VolumeDriveLetters;
        var events = await _eventLogReader
            .ReadUsbEventsAsync(since, driveLetters, cancellationToken)
            .ConfigureAwait(false);

        _sessionBuilder.ApplyEvents(session, events);
    }

    /// <summary>
    /// Finalises sessions whose cooldown has elapsed, scores risk, and enqueues payloads.
    /// </summary>
    private async Task FinalizeClosedSessionsAsync(
        HostInfo host,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var session in _sessionBuilder.GetActiveSessions().ToList())
        {
            if (session.DisconnectedAt is null ||
                session.CooldownUntil is null ||
                now < session.CooldownUntil.Value)
            {
                continue;
            }

            var (score, reasons) = _riskScoring.Score(session);
            var payload = BuildPayload(host, session, score, reasons);
            var json = JsonSerializer.Serialize(payload, SerializerOptions);

            await _queueStore.EnqueueAsync(json, cancellationToken).ConfigureAwait(false);
            session.Finalized = true;
            _sessionBuilder.RemoveSession(session.SessionId);

            _logger.LogInformation(
                "Session {SessionId} finalised with risk {RiskScore} and enqueued",
                session.SessionId,
                score);
        }
    }

    /// <summary>
    /// Builds a camelCase ingest payload for a closed session.
    /// </summary>
    private UsbSessionPayload BuildPayload(
        HostInfo host,
        UsbSessionState session,
        int riskScore,
        IReadOnlyList<RiskReason> reasons)
    {
        return new UsbSessionPayload
        {
            CorpCode = _options.CorpCode,
            Host = host,
            Session = new UsbSessionDetail
            {
                SessionId = session.SessionId,
                DeviceSerial = session.DeviceSerial,
                Vendor = session.Vendor,
                Model = session.Model,
                ConnectedAt = session.ConnectedAt,
                DisconnectedAt = session.DisconnectedAt,
                TotalBytesWritten = session.TotalBytesWritten,
                RiskScore = riskScore,
                Reasons = reasons.ToList()
            },
            Events = session.Events
                .Select(e => new UsbEventRecord
                {
                    EventId = e.EventId,
                    RecordId = e.RecordId,
                    TimeCreated = e.TimeCreated,
                    Activity = e.Activity,
                    Description = e.Description
                })
                .ToList(),
            AuditSettingsStatus = session.AuditSettings
        };
    }

    /// <summary>
    /// Drains the SQLite queue in FIFO order with per-item back-off on failure.
    /// </summary>
    private async Task DrainQueueAsync(CancellationToken cancellationToken)
    {
        var pending = await _queueStore
            .DequeuePendingAsync(_options.MaxBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        var payloads = pending.Select(p => p.PayloadJson).ToList();
        var success = await _ingestApiClient
            .SendBatchAsync(payloads, cancellationToken)
            .ConfigureAwait(false);

        if (success)
        {
            foreach (var item in pending)
            {
                await _queueStore.MarkDeliveredAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var item in pending)
        {
            await _queueStore
                .MarkFailedAsync(item.Id, item.AttemptCount + 1, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Masks most of a device serial for safe logging.
    /// </summary>
    private static string MaskSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length <= 4)
        {
            return "****";
        }

        return new string('*', serial.Length - 4) + serial[^4..];
    }
}
