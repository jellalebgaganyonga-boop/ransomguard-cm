namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Snapshot of a running process captured at a point in time.
/// Used for forensic analysis in GENEALOGY alert enrichment.
/// </summary>
public sealed record ProcessSnapshot
{
    /// <summary>Process ID.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Parent process ID.</summary>
    public required int ParentProcessId { get; init; }

    /// <summary>Process name (e.g., "powershell").</summary>
    public required string ProcessName { get; init; }

    /// <summary>Full path to the executable, if accessible.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Command line arguments, if accessible via WMI.</summary>
    public string? CommandLine { get; init; }

    /// <summary>Process start time (UTC).</summary>
    public DateTime? StartTime { get; init; }

    /// <summary>User account running the process.</summary>
    public string? UserAccount { get; init; }

    /// <summary>Working set memory in bytes.</summary>
    public long WorkingSetBytes { get; init; }
}
