using USBInfilteratorDetectorService.Models;

namespace USBInfilteratorDetectorService.Services.Interfaces;

/// <summary>
/// Opens, updates, and closes in-memory USB sessions.
/// </summary>
public interface ISessionBuilder
{
    /// <summary>
    /// Returns all active or cooling-down sessions.
    /// </summary>
    IReadOnlyCollection<UsbSessionState> GetActiveSessions();

    /// <summary>
    /// Opens a new session for a newly connected USB device.
    /// </summary>
    UsbSessionState OpenSession(UsbDeviceInfo device, DateTimeOffset connectedAt);

    /// <summary>
    /// Finds an existing session by device key, or null if none.
    /// </summary>
    UsbSessionState? FindSession(string deviceKey);

    /// <summary>
    /// Marks a session as disconnected and starts the cooldown window.
    /// </summary>
    void BeginCooldown(UsbSessionState session, DateTimeOffset disconnectedAt, bool abruptRemoval, TimeSpan cooldown);

    /// <summary>
    /// Appends new events and accumulates bytes written for a session.
    /// </summary>
    void ApplyEvents(UsbSessionState session, IEnumerable<UsbEventRecord> events);

    /// <summary>
    /// Removes a finalized session from in-memory tracking.
    /// </summary>
    void RemoveSession(Guid sessionId);
}
