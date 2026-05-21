using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Tracks per-process and per-destination data volumes with sliding windows.
/// Used by detection rules to identify anomalous transfer patterns.
/// </summary>
public interface IDataVolumeTracker
{
    /// <summary>
    /// Records a network event, updating byte counters and connection state.
    /// </summary>
    void RecordEvent(NetworkEvent networkEvent);

    /// <summary>
    /// Gets the total bytes sent by a process in the specified window.
    /// </summary>
    long GetBytesSent(int processId, TimeSpan window);

    /// <summary>
    /// Gets the total bytes sent to a destination in the specified window.
    /// </summary>
    long GetBytesSentToDestination(string destination, TimeSpan window);

    /// <summary>
    /// Gets the DNS query count for a process in the specified window.
    /// </summary>
    int GetDnsQueryCount(int processId, TimeSpan window);

    /// <summary>
    /// Gets all DNS queries for a process in the specified window.
    /// </summary>
    IReadOnlyList<string> GetDnsQueries(int processId, TimeSpan window);

    /// <summary>
    /// Gets all active connections for a process.
    /// </summary>
    IReadOnlyList<NetworkEvent> GetActiveConnections(int processId);

    /// <summary>
    /// Gets the baseline average bytes/hour for a destination (from learning phase).
    /// Returns null if no baseline exists.
    /// </summary>
    long? GetBaselineBytesSentPerHour(string destination);

    /// <summary>
    /// Whether the tracker is currently in learning phase.
    /// </summary>
    bool IsLearningPhase { get; }

    /// <summary>
    /// Gets the total unique destinations contacted by a process in the specified window.
    /// </summary>
    int GetUniqueDestinationCount(int processId, TimeSpan window);

    /// <summary>
    /// Gets the total bytes sent by all processes to cloud provider IPs in the specified window.
    /// </summary>
    long GetCloudUploadBytes(TimeSpan window);
}
