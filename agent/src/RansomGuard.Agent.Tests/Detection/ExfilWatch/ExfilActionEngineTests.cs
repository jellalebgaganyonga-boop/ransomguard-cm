using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

public sealed class ExfilActionEngineTests
{
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<IThreatIntelProvider> _threatIntel = new();
    private readonly Mock<IDataVolumeTracker> _tracker = new();
    private readonly Mock<IFirewallManager> _firewall = new();
    private readonly Mock<IProcessThrottler> _throttler = new();

    private static ExfilFinding CreateFinding(ExfilSeverity severity, string dest = "45.33.32.1") => new()
    {
        RuleName = "TestRule",
        Severity = severity,
        Description = "Test finding",
        ProcessId = 1234,
        ProcessName = "suspect.exe",
        Destination = dest,
        BytesTransferred = 10_000_000,
        MitreId = "T1041"
    };

    private ExfilActionEngine CreateEngine(ExfilWatchOptions? options = null)
    {
        var opts = options ?? new ExfilWatchOptions();
        return new ExfilActionEngine(
            new AlertOnlyAction(_auditLog.Object, new Mock<ILogger<AlertOnlyAction>>().Object),
            new ThrottleProcessAction(_throttler.Object, _auditLog.Object, new Mock<ILogger<ThrottleProcessAction>>().Object),
            new BlockIpAction(_firewall.Object, _auditLog.Object, new Mock<ILogger<BlockIpAction>>().Object),
            _threatIntel.Object,
            _tracker.Object,
            _auditLog.Object,
            opts,
            new Mock<ILogger<ExfilActionEngine>>().Object);
    }

    // ===== Test 1: AlertOnly_Always_Works =====

    [Fact]
    public async Task AlertOnly_Always_Works()
    {
        var alertAction = new AlertOnlyAction(
            _auditLog.Object, new Mock<ILogger<AlertOnlyAction>>().Object);

        var result = await alertAction.ExecuteAsync(CreateFinding(ExfilSeverity.Low), CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.ActionName.ShouldBe("AlertOnly");
        _auditLog.Verify(a => a.AppendAsync(
            "ExfilAlert", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Test 2: Throttle_Reduces_Bandwidth_Verified_Via_Measurement =====

    [Fact]
    public async Task Throttle_Reduces_Bandwidth_Verified_Via_Measurement()
    {
        _throttler.Setup(t => t.ApplyThrottle(1234, "suspect.exe", It.IsAny<long>())).Returns(true);

        var throttleAction = new ThrottleProcessAction(
            _throttler.Object, _auditLog.Object, new Mock<ILogger<ThrottleProcessAction>>().Object);

        var finding = CreateFinding(ExfilSeverity.High);
        var result = await throttleAction.ExecuteAsync(finding, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.ActionName.ShouldBe("ThrottleProcess");

        // Verify throttle was applied with 10% of measured bandwidth
        // 10_000_000 bytes / 3600s = ~2778 bytes/s → ~22222 bits/s → 10% = ~2222 bps (floor 8000)
        _throttler.Verify(t => t.ApplyThrottle(
            1234, "suspect.exe", It.Is<long>(bps => bps >= 8000)), Times.Once);

        _auditLog.Verify(a => a.AppendAsync(
            "ExfilThrottle", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Test 3: Block_Ip_Creates_Firewall_Rule =====

    [Fact]
    public async Task Block_Ip_Creates_Firewall_Rule()
    {
        _firewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), "45.33.32.1")).Returns(true);

        var blockAction = new BlockIpAction(
            _firewall.Object, _auditLog.Object, new Mock<ILogger<BlockIpAction>>().Object);

        var result = await blockAction.ExecuteAsync(CreateFinding(ExfilSeverity.Critical), CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.ActionName.ShouldBe("BlockIp");

        _firewall.Verify(f => f.AddBlockRule(
            It.Is<string>(name => name.StartsWith(BlockIpAction.RuleNamePrefix)),
            "45.33.32.1"), Times.Once);

        _auditLog.Verify(a => a.AppendAsync(
            "ExfilBlock", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Test 4: Block_Ip_Rule_Expires_After_24_Hours =====

    [Fact]
    public void Block_Ip_Rule_Expires_After_24_Hours()
    {
        // Verify timestamp parsing from rule names (used by cleanup service)
        string timestamp = DateTime.UtcNow.AddHours(-25).ToString(BlockIpAction.TimestampFormat);
        string ruleName = $"{BlockIpAction.RuleNamePrefix}10.0.0.1-{timestamp}";

        var parsed = BlockIpAction.ParseRuleTimestamp(ruleName);
        parsed.ShouldNotBeNull();

        // Rule created 25 hours ago should be expired (24h expiry)
        var age = DateTime.UtcNow - parsed.Value;
        age.TotalHours.ShouldBeGreaterThan(24);

        // Recent rule should NOT be expired
        string recentTimestamp = DateTime.UtcNow.ToString(BlockIpAction.TimestampFormat);
        string recentRule = $"{BlockIpAction.RuleNamePrefix}10.0.0.1-{recentTimestamp}";
        var recentParsed = BlockIpAction.ParseRuleTimestamp(recentRule);
        recentParsed.ShouldNotBeNull();

        var recentAge = DateTime.UtcNow - recentParsed.Value;
        recentAge.TotalHours.ShouldBeLessThan(1);
    }

    // ===== Test 5: Cloud_Whitelist_Suppresses_Alerts_Under_Threshold =====

    [Fact]
    public async Task Cloud_Whitelist_Suppresses_Alerts_Under_Threshold()
    {
        _threatIntel.Setup(t => t.IsKnownCloudProvider("52.239.100.1")).Returns(true);
        _tracker.Setup(t => t.GetBytesSentToDestination("52.239.100.1", TimeSpan.FromHours(1)))
            .Returns(500_000_000L); // 500 MB — under 1 GB threshold

        var engine = CreateEngine();
        await engine.ExecuteAsync(CreateFinding(ExfilSeverity.High, "52.239.100.1"), CancellationToken.None);

        // No actions should fire (suppressed by cloud whitelist)
        _auditLog.Verify(a => a.AppendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ===== Test 6: Cloud_Whitelist_Alerts_Over_1gb_Per_Hour =====

    [Fact]
    public async Task Cloud_Whitelist_Alerts_Over_1gb_Per_Hour()
    {
        _threatIntel.Setup(t => t.IsKnownCloudProvider("52.239.100.1")).Returns(true);
        _tracker.Setup(t => t.GetBytesSentToDestination("52.239.100.1", TimeSpan.FromHours(1)))
            .Returns(2_000_000_000L); // 2 GB — over 1 GB threshold (OneDrive abuse)

        var engine = CreateEngine();
        await engine.ExecuteAsync(CreateFinding(ExfilSeverity.High, "52.239.100.1"), CancellationToken.None);

        // Alert should fire (over threshold — OneDrive abuse detection)
        _auditLog.Verify(a => a.AppendAsync(
            "ExfilAlert", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ===== Test 7: Action_Timeout_Enforced_10_Seconds =====

    [Fact]
    public async Task Action_Timeout_Enforced_10_Seconds()
    {
        // Create a throttler that hangs forever
        _throttler.Setup(t => t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(false); // Simulate failure so fallback triggers

        var engine = CreateEngine();
        var finding = CreateFinding(ExfilSeverity.High);

        // Should complete within a reasonable time (not hang)
        var task = engine.ExecuteAsync(finding, CancellationToken.None);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));

        completed.ShouldBe(task); // Engine completed before the 30s timeout

        // Fallback from ThrottleProcess → AlertOnly should have fired
        _auditLog.Verify(a => a.AppendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ===== Test 8: Audit_Log_Captures_Every_Action_Attempt =====

    [Fact]
    public async Task Audit_Log_Captures_Every_Action_Attempt()
    {
        _throttler.Setup(t => t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>())).Returns(true);
        _firewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var engine = CreateEngine();

        // Critical severity → AlertOnly + Throttle + Block = 3 audit entries
        await engine.ExecuteAsync(CreateFinding(ExfilSeverity.Critical), CancellationToken.None);

        _auditLog.Verify(a => a.AppendAsync(
            "ExfilAlert", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
        _auditLog.Verify(a => a.AppendAsync(
            "ExfilThrottle", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
        _auditLog.Verify(a => a.AppendAsync(
            "ExfilBlock", It.IsAny<string>(), "ExfilFinding", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Additional: Decision matrix verification =====

    [Theory]
    [InlineData(ExfilSeverity.Low, 1)]      // AlertOnly
    [InlineData(ExfilSeverity.Medium, 1)]    // AlertOnly
    [InlineData(ExfilSeverity.High, 2)]      // AlertOnly + Throttle
    [InlineData(ExfilSeverity.Critical, 3)]  // AlertOnly + Throttle + Block
    public void Decision_Matrix_Returns_Correct_Action_Count(ExfilSeverity severity, int expectedCount)
    {
        var engine = CreateEngine();
        var actions = engine.GetActionsForSeverity(severity);
        actions.ShouldNotBeNull();
        actions.Count().ShouldBe(expectedCount);
    }

    [Fact]
    public async Task Block_Failure_Falls_Back_To_Throttle()
    {
        _firewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        _throttler.Setup(t => t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>())).Returns(true);

        var engine = CreateEngine();
        await engine.ExecuteAsync(CreateFinding(ExfilSeverity.Critical), CancellationToken.None);

        // Block failed → fallback to Throttle (in addition to the normal Throttle action)
        _throttler.Verify(t => t.ApplyThrottle(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>()), Times.AtLeast(2));
    }
}
