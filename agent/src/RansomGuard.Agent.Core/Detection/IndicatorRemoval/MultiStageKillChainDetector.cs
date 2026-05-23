using System.Collections.Concurrent;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Correlates multiple indicator removal events within a 5-minute window
/// to detect ransomware kill chain patterns.
/// Pattern: vssadmin delete shadows + wevtutil cl + fsutil usn deletejournal = Critical kill chain.
/// </summary>
public sealed class MultiStageKillChainDetector
{
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentQueue<TimestampedEvent> _recentEvents = new();
    private long _correlationCount;

    /// <summary>Number of kill chain correlations detected.</summary>
    public long CorrelationCount => Interlocked.Read(ref _correlationCount);

    /// <summary>
    /// Records an indicator removal event and checks for kill chain correlation.
    /// Returns a correlation event if the kill chain pattern is detected, null otherwise.
    /// </summary>
    public IndicatorRemovalEvent? RecordAndCorrelate(IndicatorRemovalEvent evt)
    {
        if (evt.WhitelistSuppressed)
            return null;

        _recentEvents.Enqueue(new TimestampedEvent(evt.DetectedAt, evt.EventType, evt.ProcessName));
        TrimOldEvents();

        if (!IsKillChainDetected())
            return null;

        Interlocked.Increment(ref _correlationCount);
        var correlationId = Guid.NewGuid();

        return new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.KillChainCorrelation,
            MitreTechniqueId = "T1070+T1490",
            ProcessId = 0,
            ProcessName = "MULTI-STAGE",
            CommandLine = "kill chain correlation",
            TargetResource = "vssadmin + wevtutil + fsutil",
            Severity = "Critical",
            Description = $"RANSOMWARE KILL CHAIN: Volume shadow deletion + event log clearing + " +
                          $"USN journal clearing detected within {CorrelationWindow.TotalMinutes} minutes",
            ActionTaken = "Alert",
            KillChainCorrelationId = correlationId
        };
    }

    /// <summary>
    /// Checks if recent events include the classic ransomware kill chain:
    /// 1. Volume shadow deletion (vssadmin — detected by existing SuspiciousPatternDetector)
    /// 2. Event log clearing (wevtutil cl)
    /// 3. USN journal clearing (fsutil usn deletejournal)
    /// At least 2 of 3 stages must be present within the correlation window.
    /// </summary>
    private bool IsKillChainDetected()
    {
        var cutoff = DateTime.UtcNow - CorrelationWindow;
        var recent = _recentEvents.Where(e => e.Timestamp >= cutoff).ToList();

        bool hasEventLogClearing = recent.Any(e => e.Type == IndicatorRemovalType.EventLogClearing);
        bool hasUsnJournalClearing = recent.Any(e => e.Type == IndicatorRemovalType.UsnJournalClearing);
        bool hasDefenderTampering = recent.Any(e => e.Type == IndicatorRemovalType.DefenderTampering);
        bool hasSchedTaskTampering = recent.Any(e => e.Type == IndicatorRemovalType.ScheduledTaskTampering);

        // Kill chain: at least 2 distinct indicator removal types within the window
        int stageCount = (hasEventLogClearing ? 1 : 0) +
                         (hasUsnJournalClearing ? 1 : 0) +
                         (hasDefenderTampering ? 1 : 0) +
                         (hasSchedTaskTampering ? 1 : 0);

        return stageCount >= 2;
    }

    private void TrimOldEvents()
    {
        var cutoff = DateTime.UtcNow - CorrelationWindow - TimeSpan.FromMinutes(1);
        while (_recentEvents.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _recentEvents.TryDequeue(out _);
        }
    }

    private sealed record TimestampedEvent(DateTime Timestamp, IndicatorRemovalType Type, string ProcessName);
}
