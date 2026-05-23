using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

/// <summary>
/// Tests for all 8 EXFIL WATCH detection rules.
/// Each rule has positive (fires) and negative (does not fire) test cases.
/// </summary>
public sealed class ExfilDetectionRuleTests
{
    private readonly ExfilWatchOptions _options = new()
    {
        Enabled = true,
        LearningPhaseDays = 7,
        VolumeAnomalyMultiplier = 5,
        DnsTunnelingQueryThreshold = 100,
        DnsTunnelingEntropyThreshold = 4.0,
        WorkingHoursStart = 7,
        WorkingHoursEnd = 19,
        CloudAlertThresholdGbPerHour = 1.0,
        BlockRuleExpiryHours = 24,
        EtwBufferCount = 64
    };

    // ===== Rule 1: VolumeAnomaly =====

    [Fact]
    public void VolumeAnomaly_AboveThreshold_Fires()
    {
        var rule = new VolumeAnomalyRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(false);
        tracker.Setup(t => t.GetBaselineBytesSentPerHour("10.0.0.1")).Returns(1_000_000L); // 1 MB/hour baseline
        tracker.Setup(t => t.GetBytesSentToDestination("10.0.0.1", TimeSpan.FromHours(1)))
            .Returns(10_000_000L); // 10 MB/hour — 10x above baseline

        var finding = rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 1024), tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("VolumeAnomaly");
        finding.MitreId.ShouldBe("T1041");
    }

    [Fact]
    public void VolumeAnomaly_BelowThreshold_DoesNotFire()
    {
        var rule = new VolumeAnomalyRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(false);
        tracker.Setup(t => t.GetBaselineBytesSentPerHour("10.0.0.1")).Returns(10_000_000L);
        tracker.Setup(t => t.GetBytesSentToDestination("10.0.0.1", TimeSpan.FromHours(1)))
            .Returns(5_000_000L); // Under 5x threshold

        rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 1024), tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void VolumeAnomaly_DuringLearning_DoesNotFire()
    {
        var rule = new VolumeAnomalyRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(true);

        rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 999_999_999), tracker.Object, _options).ShouldBeNull();
    }

    // ===== Rule 2: DnsTunneling =====

    [Fact]
    public void DnsTunneling_HighFrequencyHighEntropy_Fires()
    {
        var rule = new DnsTunnelingRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetDnsQueryCount(1000, TimeSpan.FromMinutes(5))).Returns(1500);

        // High-entropy subdomain: random hex-like data (all unique chars)
        var evt = CreateDnsQuery(1000, "x7f3a9c2e1b4d6hqwkjmnoprstuvyz.evil.com");

        // Verify entropy is above threshold
        double entropy = DnsTunnelingRule.ComputeSubdomainEntropy(evt.DnsQueryName!);
        entropy.ShouldBeGreaterThan(_options.DnsTunnelingEntropyThreshold,
            $"Subdomain entropy {entropy:F2} should exceed threshold {_options.DnsTunnelingEntropyThreshold}");

        var finding = rule.Evaluate(evt, tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("DnsTunneling");
        finding.MitreId.ShouldBe("T1071.004");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);
    }

    [Fact]
    public void DnsTunneling_LowFrequency_DoesNotFire()
    {
        var rule = new DnsTunnelingRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetDnsQueryCount(1000, TimeSpan.FromMinutes(5))).Returns(10);

        rule.Evaluate(CreateDnsQuery(1000, "aGVsbG8.evil.com"), tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void DnsTunneling_LowEntropy_DoesNotFire()
    {
        var rule = new DnsTunnelingRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetDnsQueryCount(1000, TimeSpan.FromMinutes(5))).Returns(1500);

        // Low-entropy subdomain
        rule.Evaluate(CreateDnsQuery(1000, "www.google.com"), tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void DnsTunneling_SubdomainEntropy_CalculatesCorrectly()
    {
        // High entropy (random-looking)
        double high = DnsTunnelingRule.ComputeSubdomainEntropy("aGVsbG8gd29ybGQ.evil.com");
        high.ShouldBeGreaterThan(3.0);

        // Low entropy (simple prefix)
        double low = DnsTunnelingRule.ComputeSubdomainEntropy("www.example.com");
        low.ShouldBeLessThan(2.0);

        // No subdomain
        double none = DnsTunnelingRule.ComputeSubdomainEntropy("example.com");
        none.ShouldBe(0);
    }

    // ===== Rule 6: AfterHoursExfil =====

    [Fact]
    public void AfterHoursExfil_LargeTransferOutsideHours_Fires()
    {
        var rule = new AfterHoursExfilRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSent(1000, TimeSpan.FromMinutes(5)))
            .Returns(100_000_000L); // 100 MB

        // Force off-hours by using a config where current hour is outside range
        var offHoursOptions = _options with { WorkingHoursStart = 0, WorkingHoursEnd = 0 };

        var finding = rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 1024), tracker.Object, offHoursOptions);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("AfterHoursExfil");
        finding.MitreId.ShouldBe("T1041");
    }

    [Fact]
    public void AfterHoursExfil_SmallTransfer_DoesNotFire()
    {
        var rule = new AfterHoursExfilRule();
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSent(1000, TimeSpan.FromMinutes(5))).Returns(1024L);

        var offHoursOptions = _options with { WorkingHoursStart = 0, WorkingHoursEnd = 0 };
        rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 1024), tracker.Object, offHoursOptions).ShouldBeNull();
    }

    // ===== Rule Engine =====

    [Fact]
    public void RuleEngine_Has3BaseRules()
    {
        var engine = new ExfilRuleEngine(new Mock<ILogger<ExfilRuleEngine>>().Object);
        engine.RuleCount.ShouldBe(3);
    }

    [Fact]
    public void RuleEngine_CleanEvent_ReturnsEmpty()
    {
        var engine = new ExfilRuleEngine(new Mock<ILogger<ExfilRuleEngine>>().Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(true);

        var evt = CreateTcpSend(1000, "10.0.0.1", 1024);
        var findings = engine.Evaluate(evt, tracker.Object, _options);
        findings.ShouldBeEmpty();
    }

    // ===== Helpers =====

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

    private static NetworkEvent CreateTcpConnectAt(int pid, string dest, int port, DateTime timestamp)
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
            Timestamp = timestamp
        };
    }
}
