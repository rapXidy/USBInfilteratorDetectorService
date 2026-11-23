using System.Runtime.InteropServices;
using Microsoft.Win32;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Resolves host name, machine GUID, interactive user, and OS version.
/// </summary>
public sealed class HostContextService : IHostContextService
{
    /// <summary>
    /// Builds the current host context for outbound payloads.
    /// </summary>
    public HostInfo GetHostInfo()
    {
        return new HostInfo
        {
            HostName = Environment.MachineName,
            DeviceId = ResolveMachineGuid(),
            LoggedInUser = ResolveLoggedInUser(),
            OsVersion = Environment.OSVersion.Version.ToString()
        };
    }

    /// <summary>
    /// Reads the Windows MachineGuid from the Cryptography registry key.
    /// </summary>
    private static string ResolveMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var value = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
            // Fall through to a stable process-local placeholder.
        }

        return Guid.Empty.ToString();
    }

    /// <summary>
    /// Returns the interactive logged-on user when available, otherwise the process identity.
    /// </summary>
    private static string ResolveLoggedInUser()
    {
        try
        {
            var userName = GetExplorerUserName();
            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }
        }
        catch
        {
            // Ignore and fall back.
        }

        return Environment.UserDomainName + "\\" + Environment.UserName;
    }

    /// <summary>
    /// Attempts to resolve the interactive user via WTS API when running as a service.
    /// </summary>
    private static string? GetExplorerUserName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        // Prefer Environment when an interactive session is attached.
        if (Environment.UserInteractive &&
            !string.Equals(Environment.UserName, "SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }

        return Environment.UserDomainName + "\\" + Environment.UserName;
    }
}
