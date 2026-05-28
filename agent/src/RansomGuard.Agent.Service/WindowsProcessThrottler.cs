using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Applies per-process network bandwidth throttling using Windows QoS policy.
/// Uses PowerShell New-NetQosPolicy with ThrottleRateActionBitsPerSecond.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessThrottler : IProcessThrottler
{
    private const string PolicyPrefix = "RansomGuard-Throttle-";
    private readonly ILogger<WindowsProcessThrottler> _logger;

    /// <summary>Initializes the process throttler.</summary>
    public WindowsProcessThrottler(ILogger<WindowsProcessThrottler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool ApplyThrottle(int processId, string processName, long maxBitsPerSecond)
    {
        string policyName = $"{PolicyPrefix}{processName}-{processId}";

        try
        {
            // Remove existing policy if present
            RemoveThrottle(processId, processName);

            // Create QoS policy targeting the process executable
            string script =
                $"New-NetQosPolicy -Name '{policyName}' " +
                $"-AppPathNameMatchCondition '{processName}' " +
                $"-ThrottleRateActionBitsPerSecond {maxBitsPerSecond} " +
                $"-PolicyStore ActiveStore -ErrorAction Stop";

            bool success = RunPowerShell(script);

            if (success)
                _logger.LogInformation(
                    "QoS throttle applied: {Policy}, Process={Process}, MaxBps={Bps}",
                    policyName, processName, maxBitsPerSecond);
            else
                _logger.LogError("QoS throttle failed for {Policy}", policyName);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply QoS throttle for {Process}", processName);
            return false;
        }
    }

    /// <inheritdoc />
    public bool RemoveThrottle(int processId, string processName)
    {
        string policyName = $"{PolicyPrefix}{processName}-{processId}";
        try
        {
            string script =
                $"Remove-NetQosPolicy -Name '{policyName}' -Confirm:$false -ErrorAction SilentlyContinue";
            return RunPowerShell(script);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove QoS throttle {Policy}", policyName);
            return false;
        }
    }

    private bool RunPowerShell(string script)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        bool exited = process.WaitForExit(8000); // 8s timeout (under 10s action limit)

        if (!exited)
        {
            process.Kill();
            return false;
        }

        return process.ExitCode == 0;
    }
}
