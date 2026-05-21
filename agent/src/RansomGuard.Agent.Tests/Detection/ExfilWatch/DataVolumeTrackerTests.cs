using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

/// <summary>
/// Tests for DataVolumeTracker: sliding windows, per-process/destination tracking,
/// learning phase baselines, cloud detection, concurrency.
/// </summary>
public sealed class DataVolumeTrackerTests
{
    private readonly DataVolumeTracker _tracker;

    public DataVolumeTrackerTests()
    {
        _tracker = new DataVolumeTracker(
            new Mock<ILogger<DataVolumeTracker>>().Object,
            learningPhaseDays: 7);
    }

    [Fact]
    public void RecordEvent_IncrementsTotalCount()
    {
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.1", 1024));
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.1", 2048));

        _tracker.TotalEventsRecorded.ShouldBe(2);
    }

    [Fact]
    public void GetBytesSent_PerProcess_SumsCorrectly()
    {
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.1", 1024));
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.2", 2048));
        _tracker.RecordEvent(CreateTcpSend(2000, "10.0.0.1", 4096)); // Different PID

        _tracker.GetBytesSent(1000, TimeSpan.FromMinutes(1)).ShouldBe(3072);
        _tracker.GetBytesSent(2000, TimeSpan.FromMinutes(1)).ShouldBe(4096);
    }

    [Fact]
    public void GetBytesSent_UnknownProcess_ReturnsZero()
    {
        _tracker.GetBytesSent(99999, TimeSpan.FromMinutes(1)).ShouldBe(0);
    }

    [Fact]
    public void GetBytesSentToDestination_SumsAcrossProcesses()
    {
        _tracker.RecordEvent(CreateTcpSend(1000, "203.0.113.50", 1000));
        _tracker.RecordEvent(CreateTcpSend(2000, "203.0.113.50", 2000));
        _tracker.RecordEvent(CreateTcpSend(1000, "198.51.100.1", 5000)); // Different dest

        _tracker.GetBytesSentToDestination("203.0.113.50", TimeSpan.FromMinutes(5)).ShouldBe(3000);
        _tracker.GetBytesSentToDestination("198.51.100.1", TimeSpan.FromMinutes(5)).ShouldBe(5000);
    }

    [Fact]
    public void GetBytesSentToDestination_UnknownDest_ReturnsZero()
    {
        _tracker.GetBytesSentToDestination("1.2.3.4", TimeSpan.FromMinutes(1)).ShouldBe(0);
    }

    [Fact]
    public void GetDnsQueryCount_TracksPerProcess()
    {
        _tracker.RecordEvent(CreateDnsQuery(1000, "evil.com"));
        _tracker.RecordEvent(CreateDnsQuery(1000, "safe.com"));
        _tracker.RecordEvent(CreateDnsQuery(1000, "another.com"));
        _tracker.RecordEvent(CreateDnsQuery(2000, "different.com"));

        _tracker.GetDnsQueryCount(1000, TimeSpan.FromMinutes(5)).ShouldBe(3);
        _tracker.GetDnsQueryCount(2000, TimeSpan.FromMinutes(5)).ShouldBe(1);
    }

    [Fact]
    public void GetDnsQueries_ReturnsDomainNames()
    {
        _tracker.RecordEvent(CreateDnsQuery(1000, "a.evil.com"));
        _tracker.RecordEvent(CreateDnsQuery(1000, "b.evil.com"));

        var queries = _tracker.GetDnsQueries(1000, TimeSpan.FromMinutes(5));
        queries.Count.ShouldBe(2);
        queries.ShouldContain("a.evil.com");
        queries.ShouldContain("b.evil.com");
    }

    [Fact]
    public void GetActiveConnections_ReturnsRecentTcpConnects()
    {
        _tracker.RecordEvent(CreateTcpConnect(1000, "10.0.0.1", 443));
        _tracker.RecordEvent(CreateTcpConnect(1000, "10.0.0.2", 80));

        var connections = _tracker.GetActiveConnections(1000);
        connections.Count.ShouldBe(2);
    }

    [Fact]
    public void GetUniqueDestinationCount_CountsDistinct()
    {
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.1", 100));
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.2", 200));
        _tracker.RecordEvent(CreateTcpSend(1000, "10.0.0.1", 300)); // Duplicate dest

        _tracker.GetUniqueDestinationCount(1000, TimeSpan.FromMinutes(5)).ShouldBe(2);
    }

    [Fact]
    public void IsLearningPhase_TrueAtStart()
    {
        _tracker.IsLearningPhase.ShouldBeTrue();
    }

    [Fact]
    public void IsLearningPhase_FalseAfterExpiry()
    {
        // Create tracker with 0-day learning phase (already expired)
        var tracker = new DataVolumeTracker(
            new Mock<ILogger<DataVolumeTracker>>().Object,
            learningPhaseDays: 0);

        tracker.IsLearningPhase.ShouldBeFalse();
    }

    [Fact]
    public void GetBaselineBytesSentPerHour_NullForUnknown()
    {
        _tracker.GetBaselineBytesSentPerHour("unknown.host").ShouldBeNull();
    }

    [Fact]
    public void GetBaselineBytesSentPerHour_BuildsDuringLearning()
    {
        // During learning phase, baselines should be built
        _tracker.RecordEvent(CreateTcpSend(1000, "baseline.host", 10_000));

        // Baseline is built from learning phase data
        var baseline = _tracker.GetBaselineBytesSentPerHour("baseline.host");
        baseline.ShouldNotBeNull();
        baseline.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GetCloudUploadBytes_DetectsCloudProviderIps()
    {
        // AWS IP range (3.x)
        _tracker.RecordEvent(CreateTcpSend(1000, "3.5.140.2", 1_000_000));
        // Azure IP range (20.x)
        _tracker.RecordEvent(CreateTcpSend(1000, "20.150.2.10", 2_000_000));
        // Non-cloud IP
        _tracker.RecordEvent(CreateTcpSend(1000, "192.168.1.1", 5_000_000));

        _tracker.GetCloudUploadBytes(TimeSpan.FromHours(1)).ShouldBe(3_000_000);
    }

    [Fact]
    public async Task ConcurrentRecording_ThreadSafe()
    {
        var tasks = Enumerable.Range(0, 10).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _tracker.RecordEvent(CreateTcpSend(threadId, $"10.{threadId}.0.{i % 256}", i * 10));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        _tracker.TotalEventsRecorded.ShouldBe(1000);
    }

    [Fact]
    public void MixedEventTypes_TrackedCorrectly()
    {
        _tracker.RecordEvent(CreateDnsQuery(1000, "target.com"));
        _tracker.RecordEvent(CreateTcpConnect(1000, "93.184.216.34", 443));
        _tracker.RecordEvent(CreateTcpSend(1000, "93.184.216.34", 50_000));
        _tracker.RecordEvent(CreateTcpSend(1000, "93.184.216.34", 25_000));

        _tracker.GetDnsQueryCount(1000, TimeSpan.FromMinutes(5)).ShouldBe(1);
        _tracker.GetBytesSent(1000, TimeSpan.FromMinutes(5)).ShouldBe(75_000);
        _tracker.GetActiveConnections(1000).Count.ShouldBe(1);
        _tracker.GetUniqueDestinationCount(1000, TimeSpan.FromMinutes(5)).ShouldBe(1);
    }

    // --- Helpers ---

    private static NetworkEvent CreateTcpSend(int pid, string dest, long bytes)
    {
        return new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = pid,
            ProcessName = $"proc_{pid}",
            DestinationAddress = dest,
            DestinationPort = 443,
            BytesSent = bytes,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };
    }

    private static NetworkEvent CreateDnsQuery(int pid, string domain)
    {
        return new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.DnsQuery,
            ProcessId = pid,
            ProcessName = $"proc_{pid}",
            DnsQueryName = domain,
            DnsQueryType = "A",
            Protocol = "DNS",
            Timestamp = DateTime.UtcNow
        };
    }

    private static NetworkEvent CreateTcpConnect(int pid, string dest, int port)
    {
        return new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpConnect,
            ProcessId = pid,
            ProcessName = $"proc_{pid}",
            SourceAddress = "192.168.1.10",
            SourcePort = 49152,
            DestinationAddress = dest,
            DestinationPort = port,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };
    }
}
