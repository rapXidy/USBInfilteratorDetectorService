using System.Diagnostics;
using System.Text.RegularExpressions;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Read-only audit policy inspector. Never enables or modifies Windows audit settings.
/// </summary>
public sealed class AuditPolicyService : IAuditPolicyService
{
    private static readonly Regex SuccessFailureRegex = new(
        @"Success\s*/?\s*Failure|Success|Failure|No Auditing",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<AuditPolicyService> _logger;

    /// <summary>
    /// Creates an audit policy service.
    /// </summary>
    public AuditPolicyService(ILogger<AuditPolicyService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Queries Audit PNP Activity and Audit Removable Storage via auditpol without modifying them.
    /// </summary>
    public async Task<AuditSettingsStatus> GetAuditSettingsAsync(CancellationToken cancellationToken)
    {
        var pnpEnabled = await IsCategoryAuditingEnabledAsync("Plug and Play Events", cancellationToken)
            .ConfigureAwait(false);
        var removableEnabled = await IsCategoryAuditingEnabledAsync("Removable Storage", cancellationToken)
            .ConfigureAwait(false);

        // Fallback category names used on some Windows builds / locales.
        if (!pnpEnabled)
        {
            pnpEnabled = await IsCategoryAuditingEnabledAsync("PNP Activity", cancellationToken)
                .ConfigureAwait(false);
        }

        var status = new AuditSettingsStatus
        {
            PnpActivityEnabled = pnpEnabled,
            RemovableStorageEnabled = removableEnabled,
            DetectionComplete = pnpEnabled && removableEnabled
        };

        if (!status.DetectionComplete)
        {
            _logger.LogWarning(
                "Required audit settings are disabled – file-level detection incomplete (PNP={Pnp}, Removable={Removable})",
                status.PnpActivityEnabled,
                status.RemovableStorageEnabled);
        }

        return status;
    }

    /// <summary>
    /// Runs auditpol /get for a subcategory and returns whether Success or Failure auditing is on.
    /// </summary>
    private async Task<bool> IsCategoryAuditingEnabledAsync(string subcategory, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "auditpol.exe",
                Arguments = $"/get /subcategory:\"{subcategory}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _logger.LogWarning("Unable to start auditpol.exe for subcategory {Subcategory}", subcategory);
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _logger.LogDebug(
                    "auditpol returned {ExitCode} for subcategory {Subcategory}",
                    process.ExitCode,
                    subcategory);
                return false;
            }

            return ParseAuditingEnabled(output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query audit policy for subcategory {Subcategory}", subcategory);
            return false;
        }
    }

    /// <summary>
    /// Parses auditpol output to determine if Success and/or Failure auditing is enabled.
    /// </summary>
    private static bool ParseAuditingEnabled(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("No Auditing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = SuccessFailureRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var token = match.Value;
            if (token.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("Failure", StringComparison.OrdinalIgnoreCase))
            {
                return !token.Contains("No Auditing", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
