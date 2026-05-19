using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security.AntiTampering;

/// <summary>
/// Detects attached debuggers via Win32 APIs.
/// Production: Critical alert + audit log.
/// Development: Warning only (controlled by AllowDebuggerInDevelopment config).
/// CWE-345: Validates process integrity against debugging-based tampering.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DebuggerDetector
{
    private readonly ILogger<DebuggerDetector> _logger;
    private readonly bool _allowInDevelopment;

    /// <summary>Initializes the debugger detector.</summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="allowInDevelopment">If true, debugger presence is a warning, not critical.</param>
    public DebuggerDetector(ILogger<DebuggerDetector> logger, bool allowInDevelopment = false)
    {
        _logger = logger;
        _allowInDevelopment = allowInDevelopment;
    }

    /// <summary>
    /// Checks if a debugger is attached to the current process.
    /// Uses both managed (Debugger.IsAttached) and native (IsDebuggerPresent, CheckRemoteDebuggerPresent) APIs.
    /// </summary>
    /// <returns>True if any debugger is detected.</returns>
    public bool IsBeingDebugged()
    {
        // Managed check
        if (Debugger.IsAttached)
            return true;

        // Native local debugger check
        if (IsDebuggerPresent())
            return true;

        // Native remote debugger check
        bool remoteDebugger = false;
        try
        {
            CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref remoteDebugger);
        }
        catch
        {
            // Access denied — cannot check, assume safe
        }

        return remoteDebugger;
    }

    /// <summary>
    /// Performs a debugger check and logs the result.
    /// Returns a tampering indicator string if debugger found, null otherwise.
    /// </summary>
    public string? Check()
    {
        if (!IsBeingDebugged())
            return null;

        if (_allowInDevelopment)
        {
            _logger.LogWarning("ANTI-TAMPER: Debugger detected (development mode — warning only)");
            return null; // Not treated as tampering in dev mode
        }

        _logger.LogCritical("ANTI-TAMPER: Debugger detected in production — possible reverse engineering attack");
        return "Debugger attached to agent process";
    }

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);
}
