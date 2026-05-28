namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Abstraction for process-level network bandwidth throttling.
/// Production implementation uses Windows QoS policy APIs.
/// </summary>
public interface IProcessThrottler
{
    /// <summary>
    /// Applies a bandwidth limit to the specified process.
    /// </summary>
    /// <param name="processId">Target process ID.</param>
    /// <param name="processName">Target process name (for policy naming).</param>
    /// <param name="maxBitsPerSecond">Maximum allowed bandwidth in bits per second.</param>
    /// <returns>True if throttle was applied successfully.</returns>
    bool ApplyThrottle(int processId, string processName, long maxBitsPerSecond);

    /// <summary>
    /// Removes a previously applied throttle for the specified process.
    /// </summary>
    bool RemoveThrottle(int processId, string processName);
}
