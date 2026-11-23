using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Maintains in-memory USB session state across cron ticks.
/// </summary>
public sealed class SessionBuilder : ISessionBuilder
{
    private readonly Dictionary<string, UsbSessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>
    /// Returns all active or cooling-down sessions.
    /// </summary>
    public IReadOnlyCollection<UsbSessionState> GetActiveSessions()
    {
        lock (_sync)
        {
            return _sessions.Values.Where(s => !s.Finalized).ToList();
        }
    }

    /// <summary>
    /// Opens a new session for a newly connected USB device.
    /// </summary>
    public UsbSessionState OpenSession(UsbDeviceInfo device, DateTimeOffset connectedAt)
    {
        var session = new UsbSessionState
        {
            SessionId = Guid.NewGuid(),
            DeviceKey = device.DeviceKey,
            DeviceSerial = device.DeviceSerial,
            Vendor = device.Vendor,
            Model = device.Model,
            ConnectedAt = connectedAt
        };

        lock (_sync)
        {
            _sessions[device.DeviceKey] = session;
        }

        return session;
    }

    /// <summary>
    /// Finds an existing session by device key, or null if none.
    /// </summary>
    public UsbSessionState? FindSession(string deviceKey)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(deviceKey, out var session) && !session.Finalized
                ? session
                : null;
        }
    }

    /// <summary>
    /// Marks a session as disconnected and starts the cooldown window.
    /// </summary>
    public void BeginCooldown(
        UsbSessionState session,
        DateTimeOffset disconnectedAt,
        bool abruptRemoval,
        TimeSpan cooldown)
    {
        session.DisconnectedAt = disconnectedAt;
        session.AbruptRemoval = abruptRemoval;
        session.CooldownUntil = disconnectedAt.Add(cooldown);
    }

    /// <summary>
    /// Appends new events and accumulates bytes written for a session.
    /// </summary>
    public void ApplyEvents(UsbSessionState session, IEnumerable<UsbEventRecord> events)
    {
        foreach (var evt in events)
        {
            if (session.SeenRecordIds.Contains(evt.RecordId))
            {
                continue;
            }

            session.SeenRecordIds.Add(evt.RecordId);
            session.Events.Add(evt);

            if (evt.BytesWritten is > 0)
            {
                session.TotalBytesWritten += evt.BytesWritten.Value;
            }
        }
    }

    /// <summary>
    /// Removes a finalized session from in-memory tracking.
    /// </summary>
    public void RemoveSession(Guid sessionId)
    {
        lock (_sync)
        {
            var key = _sessions.FirstOrDefault(kv => kv.Value.SessionId == sessionId).Key;
            if (!string.IsNullOrEmpty(key))
            {
                _sessions.Remove(key);
            }
        }
    }
}
