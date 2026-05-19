using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace RansomGuard.Agent.Core.Security.AntiTampering;

/// <summary>
/// Monitors agent-related registry keys for unauthorized modification.
/// Watches service configuration and autostart entries.
/// On modification: Critical alert + audit log (does not auto-revert to avoid loops).
/// CWE-269: Detects privilege escalation via service configuration tampering.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryWatcher : IDisposable
{
    /// <summary>Service configuration registry path.</summary>
    public const string ServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\RansomGuard-CM Agent";

    /// <summary>Windows autostart registry path.</summary>
    public const string AutostartKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly ILogger<RegistryWatcher> _logger;
    private readonly List<string> _monitoredPaths;
    private Timer? _pollingTimer;
    private readonly Dictionary<string, string?> _baselineValues = new();

    /// <summary>Initializes the registry watcher.</summary>
    public RegistryWatcher(ILogger<RegistryWatcher> logger)
    {
        _logger = logger;
        _monitoredPaths = [ServiceKeyPath, AutostartKeyPath];
    }

    /// <summary>
    /// Captures baseline registry values for comparison.
    /// </summary>
    public void CaptureBaseline()
    {
        foreach (string path in _monitoredPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                string? value = key?.GetValue("ImagePath")?.ToString()
                    ?? key?.GetValue("RansomGuard-CM")?.ToString();
                _baselineValues[path] = value;
            }
            catch
            {
                _baselineValues[path] = null;
            }
        }

        _logger.LogInformation("ANTI-TAMPER: Registry baseline captured for {Count} keys", _baselineValues.Count);
    }

    /// <summary>
    /// Starts periodic polling of monitored registry keys (every 5 seconds).
    /// </summary>
    public void StartMonitoring()
    {
        CaptureBaseline();
        _pollingTimer = new Timer(_ => CheckForModifications(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Checks monitored registry keys against baseline.
    /// Returns list of modification descriptions, empty if no changes.
    /// </summary>
    public IReadOnlyList<string> CheckForModifications()
    {
        var modifications = new List<string>();

        foreach (string path in _monitoredPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                string? currentValue = key?.GetValue("ImagePath")?.ToString()
                    ?? key?.GetValue("RansomGuard-CM")?.ToString();

                if (_baselineValues.TryGetValue(path, out string? baseline) && currentValue != baseline)
                {
                    string msg = $"Registry key modified: {path} (was: {baseline ?? "null"}, now: {currentValue ?? "null"})";
                    _logger.LogCritical("ANTI-TAMPER: {Message}", msg);
                    modifications.Add(msg);

                    // Update baseline to avoid repeated alerts for same change
                    _baselineValues[path] = currentValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cannot read registry key: {Path}", path);
            }
        }

        return modifications;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }
}
