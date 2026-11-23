using System.Management;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Detects USB removable storage via WMI Win32_DiskDrive and related associations.
/// </summary>
public sealed class UsbPresenceService : IUsbPresenceService
{
    private readonly ILogger<UsbPresenceService> _logger;

    /// <summary>
    /// Creates a USB presence service.
    /// </summary>
    public UsbPresenceService(ILogger<UsbPresenceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Queries WMI for USB disk drives and maps attached volume drive letters.
    /// </summary>
    public Task<IReadOnlyList<UsbDeviceInfo>> GetConnectedUsbDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = new List<UsbDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_DiskDrive WHERE InterfaceType = 'USB' OR MediaType LIKE '%Removable%'");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is not ManagementObject disk)
                {
                    continue;
                }

                var deviceId = disk["DeviceID"]?.ToString() ?? string.Empty;
                var pnpDeviceId = disk["PNPDeviceID"]?.ToString() ?? string.Empty;
                var model = disk["Model"]?.ToString() ?? "Unknown";
                var serial = disk["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
                var manufacturer = disk["Manufacturer"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(serial))
                {
                    serial = ExtractSerialFromPnp(pnpDeviceId) ?? deviceId;
                }

                var vendor = string.IsNullOrWhiteSpace(manufacturer) ||
                             manufacturer.Equals("Standard disk drives", StringComparison.OrdinalIgnoreCase)
                    ? InferVendorFromModel(model)
                    : manufacturer;

                var driveLetters = ResolveDriveLetters(deviceId);
                var key = !string.IsNullOrWhiteSpace(pnpDeviceId) ? pnpDeviceId : deviceId;

                devices.Add(new UsbDeviceInfo
                {
                    DeviceKey = key,
                    DeviceSerial = serial,
                    Vendor = vendor,
                    Model = model,
                    PnpDeviceId = pnpDeviceId,
                    VolumeDriveLetters = driveLetters
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query USB disk drives via WMI");
        }

        return Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(devices);
    }

    /// <summary>
    /// Maps a Win32_DiskDrive DeviceID to assigned volume drive letters.
    /// </summary>
    private static IReadOnlyList<string> ResolveDriveLetters(string diskDeviceId)
    {
        if (string.IsNullOrWhiteSpace(diskDeviceId))
        {
            return Array.Empty<string>();
        }

        var letters = new List<string>();

        try
        {
            var partitionQuery =
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{EscapeWmi(diskDeviceId)}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";

            using var partitionSearcher = new ManagementObjectSearcher(partitionQuery);
            foreach (ManagementBaseObject partitionItem in partitionSearcher.Get())
            {
                if (partitionItem is not ManagementObject partition)
                {
                    continue;
                }

                var partitionId = partition["DeviceID"]?.ToString();
                if (string.IsNullOrWhiteSpace(partitionId))
                {
                    continue;
                }

                var logicalQuery =
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{EscapeWmi(partitionId)}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";

                using var logicalSearcher = new ManagementObjectSearcher(logicalQuery);
                foreach (ManagementBaseObject logicalItem in logicalSearcher.Get())
                {
                    if (logicalItem is not ManagementObject logical)
                    {
                        continue;
                    }

                    var letter = logical["DeviceID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(letter))
                    {
                        letters.Add(letter);
                    }
                }
            }
        }
        catch
        {
            // Volume mapping is best-effort; presence detection still succeeds without it.
        }

        return letters;
    }

    /// <summary>
    /// Escapes single quotes for WMI query strings.
    /// </summary>
    private static string EscapeWmi(string value) => value.Replace("'", "\\'");

    /// <summary>
    /// Attempts to extract a serial fragment from a PnP device ID.
    /// </summary>
    private static string? ExtractSerialFromPnp(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return null;
        }

        var parts = pnpDeviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : null;
    }

    /// <summary>
    /// Infers a vendor name from the disk model string when Manufacturer is generic.
    /// </summary>
    private static string InferVendorFromModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "Unknown";
        }

        var firstToken = model.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstToken ?? "Unknown";
    }
}
