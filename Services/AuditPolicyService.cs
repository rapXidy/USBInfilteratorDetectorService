using System.Diagnostics;
using System.Text.RegularExpressions;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// Queries and, when needed on USB plug-in, enables required Windows audit settings via auditpol.
/// </summary>
public sealed class AuditPolicyService : IAuditPolicyService
{
    private const string PnpSubcategory = "Plug and Play Events";
    private const string PnpFallbackSubcategory = "PNP Activity";
    private const string RemovableSubcategory = "Removable Storage";

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
        var status = await QueryStatusAsync(cancellationToken).ConfigureAwait(false);

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
    /// Ensures required audit subcategories are enabled; enables any that are off, then re-queries.
    /// </summary>
    public async Task<AuditSettingsStatus> EnsureRequiredAuditSettingsAsync(CancellationToken cancellationToken)
    {
        var status = await QueryStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.DetectionComplete)
        {
            return status;
        }

        _logger.LogInformation(
            "Required audit settings incomplete on USB plug-in; attempting enable (PNP={Pnp}, Removable={Removable})",
            status.PnpActivityEnabled,
            status.RemovableStorageEnabled);

        if (!status.PnpActivityEnabled)
        {
            var pnpSet = await SetCategoryAuditingEnabledAsync(PnpSubcategory, cancellationToken)
                .ConfigureAwait(false);
            if (!pnpSet)
            {
                await SetCategoryAuditingEnabledAsync(PnpFallbackSubcategory, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (!status.RemovableStorageEnabled)
        {
            await SetCategoryAuditingEnabledAsync(RemovableSubcategory, cancellationToken)
                .ConfigureAwait(false);
        }

        var after = await QueryStatusAsync(cancellationToken).ConfigureAwait(false);
        if (after.DetectionComplete)
        {
            _logger.LogInformation(
                "Required audit settings enabled successfully (PNP={Pnp}, Removable={Removable})",
                after.PnpActivityEnabled,
                after.RemovableStorageEnabled);
        }
        else
        {
            _logger.LogWarning(
                "Unable to fully enable required audit settings after attempt (PNP={Pnp}, Removable={Removable})",
                after.PnpActivityEnabled,
                after.RemovableStorageEnabled);
        }

        return after;
    }

    /// <summary>
    /// Queries current PNP and Removable Storage audit subcategory state.
    /// </summary>
    private async Task<AuditSettingsStatus> QueryStatusAsync(CancellationToken cancellationToken)
    {
        var pnpEnabled = await IsCategoryAuditingEnabledAsync(PnpSubcategory, cancellationToken)
            .ConfigureAwait(false);
        var removableEnabled = await IsCategoryAuditingEnabledAsync(RemovableSubcategory, cancellationToken)
            .ConfigureAwait(false);

        // Fallback category names used on some Windows builds / locales.
        if (!pnpEnabled)
        {
            pnpEnabled = await IsCategoryAuditingEnabledAsync(PnpFallbackSubcategory, cancellationToken)
                .ConfigureAwait(false);
        }

        return new AuditSettingsStatus
        {
            PnpActivityEnabled = pnpEnabled,
            RemovableStorageEnabled = removableEnabled,
            DetectionComplete = pnpEnabled && removableEnabled
        };
    }

    /// <summary>
    /// Runs auditpol /get for a subcategory and returns whether Success or Failure auditing is on.
    /// </summary>
    private async Task<bool> IsCategoryAuditingEnabledAsync(string subcategory, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunAuditpolAsync(
            $"/get /subcategory:\"{subcategory}\"",
            subcategory,
            cancellationToken).ConfigureAwait(false);

        if (exitCode is null)
        {
            return false;
        }

        if (exitCode != 0)
        {
            _logger.LogDebug(
                "auditpol returned {ExitCode} for subcategory {Subcategory}",
                exitCode,
                subcategory);
            return false;
        }

        return ParseAuditingEnabled(output);
    }

    /// <summary>
    /// Enables Success and Failure auditing for a subcategory via auditpol /set.
    /// </summary>
    private async Task<bool> SetCategoryAuditingEnabledAsync(string subcategory, CancellationToken cancellationToken)
    {
        var (exitCode, _) = await RunAuditpolAsync(
            $"/set /subcategory:\"{subcategory}\" /success:enable /failure:enable",
            subcategory,
            cancellationToken).ConfigureAwait(false);

        if (exitCode is null)
        {
            return false;
        }

        if (exitCode != 0)
        {
            _logger.LogWarning(
                "auditpol /set failed with exit code {ExitCode} for subcategory {Subcategory}",
                exitCode,
                subcategory);
            return false;
        }

        _logger.LogInformation("Enabled audit subcategory {Subcategory}", subcategory);
        return true;
    }

    /// <summary>
    /// Starts auditpol.exe with the given arguments and returns exit code plus stdout.
    /// </summary>
    private async Task<(int? ExitCode, string Output)> RunAuditpolAsync(
        string arguments,
        string subcategory,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "auditpol.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _logger.LogWarning("Unable to start auditpol.exe for subcategory {Subcategory}", subcategory);
                return (null, string.Empty);
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run auditpol for subcategory {Subcategory}", subcategory);
            return (null, string.Empty);
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
