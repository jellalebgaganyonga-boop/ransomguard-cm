namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Captures process snapshots and builds process trees for forensic analysis.
/// </summary>
public interface IProcessSnapshotService
{
    /// <summary>
    /// Captures a snapshot of a specific process by PID.
    /// Returns null if the process has exited or is inaccessible.
    /// </summary>
    ProcessSnapshot? CaptureProcess(int pid);

    /// <summary>
    /// Builds a process tree by walking the parent chain upward from the given PID.
    /// </summary>
    /// <param name="pid">Starting process ID.</param>
    /// <param name="maxDepth">Maximum ancestor levels to walk (default 10).</param>
    /// <returns>Process tree, or null if the root process is inaccessible.</returns>
    ProcessTree? BuildProcessTree(int pid, int maxDepth = 10);
}
