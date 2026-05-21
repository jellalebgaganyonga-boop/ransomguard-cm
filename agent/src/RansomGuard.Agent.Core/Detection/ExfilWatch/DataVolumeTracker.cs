using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Tracks per-process and per-destination data volumes using sliding time windows.
/// Thread-safe. Supports 1-minute, 5-minute, and 1-hour window queries.
/// Learning phase builds baselines; detection phase compares against them.
/// </summary>
public sealed class DataVolumeTracker : IDataVolumeTracker
{
    private const int MaxEventsPerTracker = 100_000;
    private const int CleanupThreshold = 120_000;

    private readonly ConcurrentDictionary<int, ProcessTracker> _processByPid = new();
    private readonly ConcurrentDictionary<string, DestinationTracker> _byDestination = new();
    private readonly ConcurrentDictionary<string, long> _baselineBytesSentPerHour = new();
    private readonly ILogger<DataVolumeTracker> _logger;
    private readonly DateTime _startedAt;
    private readonly int _learningPhaseDays;
    private long _totalEventsRecorded;

    /// <inheritdoc />
    public bool IsLearningPhase => DateTime.UtcNow < _startedAt.AddDays(_learningPhaseDays);

    /// <summary>Total events recorded since tracker creation.</summary>
    public long TotalEventsRecorded => Interlocked.Read(ref _totalEventsRecorded);

    /// <summary>Initializes the data volume tracker.</summary>
    public DataVolumeTracker(ILogger<DataVolumeTracker> logger, int learningPhaseDays = 7)
    {
        _logger = logger;
        _learningPhaseDays = learningPhaseDays;
        _startedAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public void RecordEvent(NetworkEvent networkEvent)
    {
        Interlocked.Increment(ref _totalEventsRecorded);

        // Track by process
        var processTracker = _processByPid.GetOrAdd(networkEvent.ProcessId,
            _ => new ProcessTracker(networkEvent.ProcessName));
        processTracker.Record(networkEvent);

        // Track by destination (for TCP events with destination)
        if (networkEvent.DestinationAddress is not null)
        {
            var destTracker = _byDestination.GetOrAdd(networkEvent.DestinationAddress,
                _ => new DestinationTracker());
            destTracker.Record(networkEvent);
        }

        // During learning phase, update baselines
        if (IsLearningPhase && networkEvent.DestinationAddress is not null && networkEvent.BytesSent > 0)
        {
            UpdateBaseline(networkEvent.DestinationAddress);
        }
    }

    /// <inheritdoc />
    public long GetBytesSent(int processId, TimeSpan window)
    {
        if (!_processByPid.TryGetValue(processId, out var tracker))
            return 0;
        return tracker.GetBytesSent(window);
    }

    /// <inheritdoc />
    public long GetBytesSentToDestination(string destination, TimeSpan window)
    {
        if (!_byDestination.TryGetValue(destination, out var tracker))
            return 0;
        return tracker.GetBytesSent(window);
    }

    /// <inheritdoc />
    public int GetDnsQueryCount(int processId, TimeSpan window)
    {
        if (!_processByPid.TryGetValue(processId, out var tracker))
            return 0;
        return tracker.GetDnsQueryCount(window);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDnsQueries(int processId, TimeSpan window)
    {
        if (!_processByPid.TryGetValue(processId, out var tracker))
            return [];
        return tracker.GetDnsQueries(window);
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkEvent> GetActiveConnections(int processId)
    {
        if (!_processByPid.TryGetValue(processId, out var tracker))
            return [];
        return tracker.GetActiveConnections();
    }

    /// <inheritdoc />
    public long? GetBaselineBytesSentPerHour(string destination)
    {
        return _baselineBytesSentPerHour.TryGetValue(destination, out var baseline)
            ? baseline
            : null;
    }

    /// <inheritdoc />
    public int GetUniqueDestinationCount(int processId, TimeSpan window)
    {
        if (!_processByPid.TryGetValue(processId, out var tracker))
            return 0;
        return tracker.GetUniqueDestinationCount(window);
    }

    /// <inheritdoc />
    public long GetCloudUploadBytes(TimeSpan window)
    {
        long total = 0;
        foreach (var kvp in _byDestination)
        {
            if (IsCloudProviderIp(kvp.Key))
                total += kvp.Value.GetBytesSent(window);
        }
        return total;
    }

    private void UpdateBaseline(string destination)
    {
        if (!_byDestination.TryGetValue(destination, out var tracker))
            return;

        long sentLastHour = tracker.GetBytesSent(TimeSpan.FromHours(1));
        _baselineBytesSentPerHour.AddOrUpdate(destination,
            sentLastHour,
            (_, existing) => (existing + sentLastHour) / 2);
    }

    private static bool IsCloudProviderIp(string address)
    {
        // Major cloud provider IP ranges (simplified check via well-known prefixes)
        // AWS: 3.x, 13.x, 15.x, 18.x, 52.x, 54.x
        // Azure: 13.x, 20.x, 40.x, 51.x, 52.x, 104.x
        // GCP: 34.x, 35.x
        return address.StartsWith("3.") || address.StartsWith("13.") ||
               address.StartsWith("15.") || address.StartsWith("18.") ||
               address.StartsWith("20.") || address.StartsWith("34.") ||
               address.StartsWith("35.") || address.StartsWith("40.") ||
               address.StartsWith("51.") || address.StartsWith("52.") ||
               address.StartsWith("54.") || address.StartsWith("104.");
    }

    /// <summary>
    /// Per-process event tracker with sliding window queries.
    /// </summary>
    private sealed class ProcessTracker
    {
        private readonly string _processName;
        private readonly ConcurrentQueue<NetworkEvent> _events = new();
        private int _eventCount;

        public ProcessTracker(string processName) => _processName = processName;

        public void Record(NetworkEvent evt)
        {
            _events.Enqueue(evt);
            int count = Interlocked.Increment(ref _eventCount);

            // Trim old events periodically
            if (count > CleanupThreshold)
                TrimOldEvents();
        }

        public long GetBytesSent(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events.Where(e => e.Timestamp >= cutoff && e.BytesSent > 0)
                .Sum(e => e.BytesSent);
        }

        public int GetDnsQueryCount(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events.Count(e => e.Timestamp >= cutoff && e.EventType == NetworkEventType.DnsQuery);
        }

        public IReadOnlyList<string> GetDnsQueries(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events
                .Where(e => e.Timestamp >= cutoff && e.EventType == NetworkEventType.DnsQuery && e.DnsQueryName is not null)
                .Select(e => e.DnsQueryName!)
                .ToList();
        }

        public IReadOnlyList<NetworkEvent> GetActiveConnections()
        {
            // Return recent TCP connections (last 5 minutes)
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            return _events
                .Where(e => e.Timestamp >= cutoff && e.EventType == NetworkEventType.TcpConnect)
                .ToList();
        }

        public int GetUniqueDestinationCount(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events
                .Where(e => e.Timestamp >= cutoff && e.DestinationAddress is not null)
                .Select(e => e.DestinationAddress!)
                .Distinct()
                .Count();
        }

        private void TrimOldEvents()
        {
            var cutoff = DateTime.UtcNow.AddHours(-2);
            while (_events.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                if (_events.TryDequeue(out _))
                    Interlocked.Decrement(ref _eventCount);
            }
        }
    }

    /// <summary>
    /// Per-destination event tracker.
    /// </summary>
    private sealed class DestinationTracker
    {
        private readonly ConcurrentQueue<NetworkEvent> _events = new();
        private int _eventCount;

        public void Record(NetworkEvent evt)
        {
            _events.Enqueue(evt);
            int count = Interlocked.Increment(ref _eventCount);

            if (count > CleanupThreshold)
                TrimOldEvents();
        }

        public long GetBytesSent(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events.Where(e => e.Timestamp >= cutoff && e.BytesSent > 0)
                .Sum(e => e.BytesSent);
        }

        private void TrimOldEvents()
        {
            var cutoff = DateTime.UtcNow.AddHours(-2);
            while (_events.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                if (_events.TryDequeue(out _))
                    Interlocked.Decrement(ref _eventCount);
            }
        }
    }
}
