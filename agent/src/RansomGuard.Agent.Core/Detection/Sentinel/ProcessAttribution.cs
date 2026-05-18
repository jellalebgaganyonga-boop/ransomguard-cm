namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Represents a process identified as interacting with a file.
/// Captured via Windows Restart Manager API for canary alert attribution.
/// </summary>
public sealed record ProcessAttribution
{
    /// <summary>
    /// Process ID.
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Process name (e.g., "notepad").
    /// </summary>
    public required string ProcessName { get; init; }

    /// <summary>
    /// Full path to the process executable, if available.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Time the process was started, if available.
    /// </summary>
    public DateTime? StartTime { get; init; }
}
