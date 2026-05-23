using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

/// <summary>
/// Tests for 5 Sprint 4 spec rules: SuspiciousDestination, UnknownProcessExfil,
/// TorTraffic, LolbasExfil, EncryptedExfilCorrelation.
/// 3 tests per rule = 15 total.
/// </summary>
public sealed class NewExfilRuleTests
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

    // ===== Rule 2: SuspiciousDestination =====

    [Fact]
    public void SuspiciousDestination_UnknownDestLargeTransfer_Fires()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        threatIntel.Setup(t => t.IsWhitelistedDestination(It.IsAny<string>())).Returns(false);
        threatIntel.Setup(t => t.IsKnownCloudProvider(It.IsAny<string>())).Returns(false);

        var rule = new SuspiciousDestinationRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSentToDestination("198.51.100.1", TimeSpan.FromHours(1)))
            .Returns(100_000_000L); // 100 MB

        var finding = rule.Evaluate(CreateTcpSend(1000, "198.51.100.1", 5_000_000), tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("SuspiciousDestination");
        finding.MitreId.ShouldBe("T1041");
    }

    [Fact]
    public void SuspiciousDestination_WhitelistedDest_DoesNotFire()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        threatIntel.Setup(t => t.IsWhitelistedDestination("windowsupdate.microsoft.com")).Returns(true);

        var rule = new SuspiciousDestinationRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSentToDestination(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(100_000_000L);

        rule.Evaluate(CreateTcpSend(1000, "windowsupdate.microsoft.com", 5_000_000), tracker.Object, _options)
            .ShouldBeNull();
    }

    [Fact]
    public void SuspiciousDestination_BelowThreshold_DoesNotFire()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        var rule = new SuspiciousDestinationRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSentToDestination(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(1_000_000L); // 1 MB — below 50 MB threshold

        rule.Evaluate(CreateTcpSend(1000, "198.51.100.1", 1024), tracker.Object, _options).ShouldBeNull();
    }

    // ===== Rule 3: UnknownProcessExfil =====

    [Fact]
    public void UnknownProcessExfil_NewProcessLargeTransfer_Fires()
    {
        var baseline = new Mock<INetworkBaselineService>();
        baseline.Setup(b => b.IsKnownDimensionAsync("malware.exe", BaselineMetricType.ProcessNetworkVolume, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var rule = new UnknownProcessExfilRule(baseline.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(false);
        tracker.Setup(t => t.GetBytesSentToDestination("10.0.0.1", TimeSpan.FromHours(1)))
            .Returns(50_000_000L); // 50 MB

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 1000, ProcessName = "malware.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 50_000_000, Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        var finding = rule.Evaluate(evt, tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("UnknownProcessExfil");
        finding.Severity.ShouldBe(ExfilSeverity.High);
        finding.MitreId.ShouldBe("T1041");
    }

    [Fact]
    public void UnknownProcessExfil_KnownProcess_DoesNotFire()
    {
        var baseline = new Mock<INetworkBaselineService>();
        baseline.Setup(b => b.IsKnownDimensionAsync("chrome.exe", BaselineMetricType.ProcessNetworkVolume, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rule = new UnknownProcessExfilRule(baseline.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(false);
        tracker.Setup(t => t.GetBytesSentToDestination(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(50_000_000L);

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 1000, ProcessName = "chrome.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 50_000_000, Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void UnknownProcessExfil_DuringLearning_DoesNotFire()
    {
        var baseline = new Mock<INetworkBaselineService>();
        var rule = new UnknownProcessExfilRule(baseline.Object);
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.SetupGet(t => t.IsLearningPhase).Returns(true);

        rule.Evaluate(CreateTcpSend(1000, "10.0.0.1", 50_000_000), tracker.Object, _options).ShouldBeNull();
    }

    // ===== Rule 5: TorTraffic =====

    [Fact]
    public void TorTraffic_TorPort_Fires()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        threatIntel.Setup(t => t.IsTorExitNode(It.IsAny<string>())).Returns(false);

        var rule = new TorTrafficRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpConnect,
            ProcessId = 1000, ProcessName = "tor.exe",
            DestinationAddress = "192.168.1.100", DestinationPort = 9050,
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        var finding = rule.Evaluate(evt, tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("TorTraffic");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);
        finding.MitreId.ShouldBe("T1090.003");
    }

    [Fact]
    public void TorTraffic_ExitNode_Fires()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        threatIntel.Setup(t => t.IsTorExitNode("192.168.1.200")).Returns(true);

        var rule = new TorTrafficRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpConnect,
            ProcessId = 1000, ProcessName = "firefox.exe",
            DestinationAddress = "192.168.1.200", DestinationPort = 443,
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        var finding = rule.Evaluate(evt, tracker.Object, _options);
        finding.ShouldNotBeNull();
        finding.MitreId.ShouldBe("T1090.003");
    }

    [Fact]
    public void TorTraffic_NormalPort_NoExitNode_DoesNotFire()
    {
        var threatIntel = new Mock<IThreatIntelProvider>();
        threatIntel.Setup(t => t.IsTorExitNode(It.IsAny<string>())).Returns(false);

        var rule = new TorTrafficRule(threatIntel.Object);
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpConnect,
            ProcessId = 1000, ProcessName = "chrome.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    // ===== Rule 7: LolbasExfil =====

    [Fact]
    public void LolbasExfil_CertutilLargeTransfer_Fires()
    {
        var rule = new LolbasExfilRule();
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 1000, ProcessName = "certutil.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 5_000_000, // 5 MB
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        var finding = rule.Evaluate(evt, tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("LolbasExfil");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);
        finding.MitreId.ShouldBe("T1218+T1041");
    }

    [Fact]
    public void LolbasExfil_NonLolbasProcess_DoesNotFire()
    {
        var rule = new LolbasExfilRule();
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 1000, ProcessName = "chrome.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 5_000_000,
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void LolbasExfil_SmallTransfer_DoesNotFire()
    {
        var rule = new LolbasExfilRule();
        var tracker = new Mock<IDataVolumeTracker>();

        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 1000, ProcessName = "certutil.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 500_000, // 500 KB — below 1 MB threshold
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public void LolbasBinaries_Has30Entries()
    {
        LolbasBinaries.Names.Count.ShouldBe(30);
    }

    // ===== Rule 8: EncryptedExfilCorrelation =====

    [Fact]
    public async Task EncryptedExfilCorrelation_MatchingPidAndSize_Fires()
    {
        var bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        using var rule = new EncryptedExfilCorrelationRule(bus);
        var tracker = new Mock<IDataVolumeTracker>();

        // Simulate entropy signal
        var signal = new EntropySignal
        {
            SignalId = Guid.NewGuid(),
            SourceModule = "ENTROPY",
            EmittedAt = DateTime.UtcNow,
            FilePath = @"C:\data\secret.docx",
            ProcessId = 5000,
            ProcessName = "ransomware.exe",
            EntropyValue = 7.95,
            FileSize = 1_000_000, // 1 MB
            EntropyAlertId = Guid.NewGuid()
        };
        await bus.PublishAsync(signal);

        // Network event from same PID, size >= 80% of file
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 5000, ProcessName = "ransomware.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 900_000, // 90% of file size
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        var finding = rule.Evaluate(evt, tracker.Object, _options);

        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("EncryptedExfilCorrelation");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);
        finding.MitreId.ShouldBe("T1486+T1041");
        finding.Description.ShouldContain("DOUBLE EXTORTION");
        rule.LastCorrelatedEntropyAlertId.ShouldBe(signal.EntropyAlertId);
    }

    [Fact]
    public async Task EncryptedExfilCorrelation_DifferentPid_DoesNotFire()
    {
        var bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        using var rule = new EncryptedExfilCorrelationRule(bus);
        var tracker = new Mock<IDataVolumeTracker>();

        // Signal from PID 5000
        await bus.PublishAsync(new EntropySignal
        {
            SignalId = Guid.NewGuid(), SourceModule = "ENTROPY",
            EmittedAt = DateTime.UtcNow, FilePath = @"C:\data\secret.docx",
            ProcessId = 5000, ProcessName = "ransomware.exe",
            EntropyValue = 7.95, FileSize = 1_000_000,
            EntropyAlertId = Guid.NewGuid()
        });

        // Network event from different PID
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 9999, ProcessName = "other.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 900_000,
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    [Fact]
    public async Task EncryptedExfilCorrelation_TooSmallUpload_DoesNotFire()
    {
        var bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        using var rule = new EncryptedExfilCorrelationRule(bus);
        var tracker = new Mock<IDataVolumeTracker>();

        await bus.PublishAsync(new EntropySignal
        {
            SignalId = Guid.NewGuid(), SourceModule = "ENTROPY",
            EmittedAt = DateTime.UtcNow, FilePath = @"C:\data\secret.docx",
            ProcessId = 5000, ProcessName = "ransomware.exe",
            EntropyValue = 7.95, FileSize = 1_000_000,
            EntropyAlertId = Guid.NewGuid()
        });

        // Upload only 50% of file size — below 80% threshold
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(), EventType = NetworkEventType.TcpSend,
            ProcessId = 5000, ProcessName = "ransomware.exe",
            DestinationAddress = "10.0.0.1", DestinationPort = 443,
            BytesSent = 500_000, // 50% of file size
            Protocol = "TCP", Timestamp = DateTime.UtcNow
        };

        rule.Evaluate(evt, tracker.Object, _options).ShouldBeNull();
    }

    // ===== Helpers =====

    private static NetworkEvent CreateTcpSend(int pid, string dest, long bytes) => new()
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
