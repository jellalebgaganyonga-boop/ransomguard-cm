using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Detection.ThreatIntel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// E2E test: certutil-based LOLBAS exfiltration detected by Rule 7.
/// Feeds synthetic certutil network event through ExfilRuleEngine → ExfilActionEngine → audit log.
/// Uses loopback safety — no real external transfers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LolbasExfilE2E : IAsyncLifetime, IDisposable
{
    private readonly string _testDir;
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _auditRepo;
    private readonly ThreatIntelDataLoader _threatIntel;
    private readonly ExfilRuleEngine _ruleEngine;

    public LolbasExfilE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_lolbas_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var keyDir = Path.Combine(_testDir, "keys");
        Directory.CreateDirectory(keyDir);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var signer = new AuditLogSigner(keyDir, new Mock<ILogger<AuditLogSigner>>().Object);
        _auditRepo = new AuditLogRepository(_context, signer);

        _threatIntel = new ThreatIntelDataLoader(new Mock<ILogger<ThreatIntelDataLoader>>().Object);

        // Rule engine with LOLBAS rule injected
        var lolbasRule = new LolbasExfilRule();
        _ruleEngine = new ExfilRuleEngine(
            new Mock<ILogger<ExfilRuleEngine>>().Object,
            [lolbasRule]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 60_000)]
    public async Task Certutil_Exfil_Detected_And_Actioned()
    {
        // ===== PHASE 1: Synthetic certutil.exe network event (loopback safety) =====
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSent(It.IsAny<int>(), It.IsAny<TimeSpan>())).Returns(2_000_000);
        tracker.Setup(t => t.GetBytesSentToDestination(It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns(2_000_000);
        tracker.Setup(t => t.IsLearningPhase).Returns(false);

        var certutilEvent = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = 9999,
            ProcessName = "certutil.exe",
            DestinationAddress = "127.0.0.1",
            DestinationPort = 8888,
            BytesSent = 2_000_000, // 2MB — exceeds 1MB LOLBAS threshold
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        var exfilOptions = new ExfilWatchOptions();

        // ===== PHASE 2: Direct LOLBAS rule evaluation =====
        var lolbasRule = new LolbasExfilRule();
        var lolbasFinding = lolbasRule.Evaluate(certutilEvent, tracker.Object, exfilOptions);
        lolbasFinding.ShouldNotBeNull("LOLBAS rule should fire for certutil.exe");
        lolbasFinding.ProcessName.ShouldBe("certutil.exe");
        lolbasFinding.Severity.ShouldBe(ExfilSeverity.Critical);

        // ===== PHASE 4: Action engine execution =====
        var mockThrottler = new Mock<IProcessThrottler>();
        mockThrottler.Setup(t => t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>())).Returns(true);
        var mockFirewall = new Mock<IFirewallManager>();
        mockFirewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var actionEngine = new ExfilActionEngine(
            new AlertOnlyAction(_auditRepo, new Mock<ILogger<AlertOnlyAction>>().Object),
            new ThrottleProcessAction(mockThrottler.Object, _auditRepo, new Mock<ILogger<ThrottleProcessAction>>().Object),
            new BlockIpAction(mockFirewall.Object, _auditRepo, new Mock<ILogger<BlockIpAction>>().Object),
            _threatIntel,
            tracker.Object,
            _auditRepo,
            exfilOptions,
            new Mock<ILogger<ExfilActionEngine>>().Object);

        await actionEngine.ExecuteAsync(lolbasFinding, CancellationToken.None);

        // ===== ASSERTIONS =====

        // 1. LOLBAS rule detected certutil
        lolbasFinding.RuleName.ShouldBe("LolbasExfil");
        lolbasFinding.MitreId.ShouldContain("T1218");

        // 2. Severity is Critical (LOLBAS = Critical)
        lolbasFinding.Severity.ShouldBe(ExfilSeverity.Critical);

        // 3. Process attribution correct
        lolbasFinding.ProcessName.ShouldBe("certutil.exe");
        lolbasFinding.ProcessId.ShouldBe(9999);

        // 4. Action engine fired Alert + Throttle + Block (Critical severity)
        var auditEntries = await _context.AuditLogs.ToListAsync();
        auditEntries.ShouldContain(e => e.Action == "ExfilAlert");
        auditEntries.ShouldContain(e => e.Action == "ExfilThrottle");
        auditEntries.ShouldContain(e => e.Action == "ExfilBlock");

        // 5. All audit entries Ed25519 signed
        auditEntries.ShouldAllBe(e => !string.IsNullOrEmpty(e.Signature));

        // 6. Audit chain integrity
        bool chainIntact = await _auditRepo.VerifyChainIntegrityAsync();
        chainIntact.ShouldBeTrue();
    }

    public void Dispose()
    {
        Thread.Sleep(500); // Allow fire-and-forget tasks to complete
        try { _context.Database.CloseConnection(); } catch { }
        try { _context.Dispose(); } catch { }
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
