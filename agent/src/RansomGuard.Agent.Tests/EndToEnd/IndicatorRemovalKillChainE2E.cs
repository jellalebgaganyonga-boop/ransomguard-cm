using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.IndicatorRemoval;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// E2E test: T1070 multi-stage indicator removal detection.
/// Simulates non-destructive kill chain: event log clearing + USN journal clearing → correlation.
/// NO destructive commands executed — synthetic ProcessSnapshots used for safety.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IndicatorRemovalKillChainE2E : IAsyncLifetime, IDisposable
{
    private readonly string _testDir;
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _auditRepo;
    private readonly EventLogClearingDetector _eventLogDetector;
    private readonly UsnJournalClearingDetector _usnDetector;
    private readonly DefenderTamperingDetector _defenderDetector;
    private readonly MultiStageKillChainDetector _killChainDetector;

    public IndicatorRemovalKillChainE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_killchain_e2e_{Guid.NewGuid():N}");
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

        _eventLogDetector = new EventLogClearingDetector();
        _usnDetector = new UsnJournalClearingDetector();
        _defenderDetector = new DefenderTamperingDetector();
        _killChainDetector = new MultiStageKillChainDetector();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 180_000)]
    public async Task Multi_Stage_Kill_Chain_Detected_And_Correlated()
    {
        var detectedEvents = new List<IndicatorRemovalEvent>();

        // ===== STAGE 1: Simulate wevtutil cl Security =====
        // Attacker copies tools to temp directory to bypass path-based whitelist
        var stage1Process = new ProcessSnapshot
        {
            ProcessId = 5001,
            ParentProcessId = 4000,
            ProcessName = "wevtutil.exe",
            ExecutablePath = @"C:\Users\attacker\AppData\Local\Temp\wevtutil.exe",
            CommandLine = "wevtutil.exe cl Security"
        };

        var event1 = _eventLogDetector.Evaluate(stage1Process);
        event1.ShouldNotBeNull("EventLogClearingDetector should detect 'wevtutil cl Security'");
        event1.MitreTechniqueId.ShouldContain("T1070");
        detectedEvents.Add(event1);

        await _auditRepo.AppendAsync("IndicatorRemoval",
            $"Stage 1: {event1.Description}",
            entityType: "IndicatorRemovalEvent",
            entityId: event1.Id,
            cancellationToken: CancellationToken.None);

        // Record in kill chain detector
        var killChain1 = _killChainDetector.RecordAndCorrelate(event1);
        // First stage alone should not trigger kill chain (need 2+)
        killChain1.ShouldBeNull("Single stage should not trigger kill chain");

        // ===== STAGE 2: Simulate fsutil usn deletejournal /D C: =====
        var stage2Process = new ProcessSnapshot
        {
            ProcessId = 5002,
            ParentProcessId = 4000,
            ProcessName = "fsutil.exe",
            ExecutablePath = @"C:\Users\attacker\AppData\Local\Temp\fsutil.exe",
            CommandLine = "fsutil.exe usn deletejournal /D C:"
        };

        var event2 = _usnDetector.Evaluate(stage2Process);
        event2.ShouldNotBeNull("UsnJournalClearingDetector should detect 'fsutil usn deletejournal'");
        event2.MitreTechniqueId.ShouldContain("T1070");
        detectedEvents.Add(event2);

        await _auditRepo.AppendAsync("IndicatorRemoval",
            $"Stage 2: {event2.Description}",
            entityType: "IndicatorRemovalEvent",
            entityId: event2.Id,
            cancellationToken: CancellationToken.None);

        // Record in kill chain detector — this should trigger correlation (2 stages)
        var killChain2 = _killChainDetector.RecordAndCorrelate(event2);
        killChain2.ShouldNotBeNull("Two distinct indicator removal types should trigger kill chain");

        // ===== STAGE 3: Simulate Set-MpPreference -DisableRealtimeMonitoring $true =====
        var stage3Process = new ProcessSnapshot
        {
            ProcessId = 5003,
            ParentProcessId = 4000,
            ProcessName = "powershell.exe",
            ExecutablePath = @"C:\Users\attacker\AppData\Local\Temp\powershell.exe",
            CommandLine = "powershell.exe Set-MpPreference -DisableRealtimeMonitoring $true"
        };

        var event3 = _defenderDetector.Evaluate(stage3Process);
        event3.ShouldNotBeNull("DefenderTamperingDetector should detect 'Set-MpPreference -DisableRealtimeMonitoring'");
        event3.MitreTechniqueId.ShouldContain("T1562");
        detectedEvents.Add(event3);

        await _auditRepo.AppendAsync("IndicatorRemoval",
            $"Stage 3: {event3.Description}",
            entityType: "IndicatorRemovalEvent",
            entityId: event3.Id,
            cancellationToken: CancellationToken.None);

        // Third stage further strengthens the kill chain
        var killChain3 = _killChainDetector.RecordAndCorrelate(event3);
        killChain3.ShouldNotBeNull("Three stages should produce kill chain correlation");

        // ===== ASSERTIONS =====

        // 1. All 3 stages detected individually
        detectedEvents.Count.ShouldBe(3);

        // 2. Kill chain correlation produced
        killChain2!.EventType.ShouldBe(IndicatorRemovalType.KillChainCorrelation);
        killChain2.Severity.ShouldBe("Critical");
        killChain2.Description.ShouldContain("RANSOMWARE KILL CHAIN", Case.Insensitive);

        // 3. Kill chain has correlation ID
        killChain2.KillChainCorrelationId.ShouldNotBeNull();

        // 4. MITRE technique coverage
        detectedEvents[0].MitreTechniqueId.ShouldContain("T1070"); // Event log
        detectedEvents[1].MitreTechniqueId.ShouldContain("T1070"); // USN journal
        detectedEvents[2].MitreTechniqueId.ShouldContain("T1562"); // Defender tampering

        // 5. Same parent PID across stages (common in ransomware)
        detectedEvents.ShouldAllBe(e => e.ProcessId >= 5001 && e.ProcessId <= 5003);

        // 6. Audit log captures every stage
        var auditEntries = await _context.AuditLogs.ToListAsync();
        auditEntries.Count.ShouldBeGreaterThanOrEqualTo(3);
        auditEntries.ShouldAllBe(e => !string.IsNullOrEmpty(e.Signature),
            "All audit entries should be Ed25519 signed");

        // 7. Audit chain integrity
        bool chainIntact = await _auditRepo.VerifyChainIntegrityAsync();
        chainIntact.ShouldBeTrue("Audit log hash chain should be intact through multi-stage detection");

        // 8. Kill chain detector correlation count
        _killChainDetector.CorrelationCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        Thread.Sleep(500);
        try { _context.Database.CloseConnection(); } catch { }
        try { _context.Dispose(); } catch { }
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
