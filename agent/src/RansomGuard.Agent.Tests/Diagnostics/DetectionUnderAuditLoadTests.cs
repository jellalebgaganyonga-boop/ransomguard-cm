using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using Shouldly;
using Xunit.Abstractions;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// AGT-AUD-002 on the real SENTINEL monitor: the alert goes to the GRID first; a saturated or
/// broken audit neither delays nor cancels it.
/// </summary>
[Collection(RansomGuard.Agent.Tests.Persistence.AuditLoadCollection.Name)]
public sealed class DetectionUnderAuditLoadTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly MonitorHarness _h = new(chainInBackground: true);

    public DetectionUnderAuditLoadTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _h.Dispose();

    private SentinelMonitor Sentinel() => new(
        NullLogger<SentinelMonitor>.Instance,
        MonitorHarness.Config([], sentinel: new SentinelOptions
        {
            WatchDirectories = [_h.Folder],
            CanaryTemplates = ["dossier_patient"],
            CheckIntervalMs = 60_000,
            EnableRealTimeRegeneration = false,
        }),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        _h.AlertQueue,
        _h.Registry,
        _h.Reporter,
        startupDelay: TimeSpan.Zero);

    private bool Forwarded(SentinelCanary canary)
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.Any(a => a.AlertType == "SentinelCanaryModified"
                && Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_canary_modified_while_3000_audit_entries_wait_reaches_the_GRID_queue_at_once()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        _h.SaturateAudit(3000);
        await _h.ChainWriter.StartAsync(CancellationToken.None);
        using SentinelMonitor monitor = Sentinel();
        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");

        long start = Stopwatch.GetTimestamp();
        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");
        await Eventually.HoldsAsync(() => Forwarded(canary), "the alert in the GRID queue");
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        int waitingWhenSent = _h.Waiting();

        await Eventually.HoldsAsync(() => _h.Waiting() == 0 && _h.ChainedActions().Contains("CanaryAlert"),
            "the audit fully chained, the CanaryAlert entry included", timeoutMs: 90_000);
        await monitor.StopAsync(CancellationToken.None);
        await _h.ChainWriter.StopAsync(CancellationToken.None);

        _output.WriteLine($"canary write -> GRID queue: {ms:F0} ms, audit entries still waiting then: {waitingWhenSent}");
        ms.ShouldBeLessThan(2_000);
        _h.ChainedActions().ShouldContain("CanaryAlert"); // recorded, and chained in its turn
        (await _h.ChainIntactAsync()).ShouldBeTrue();
    }

    [Fact(Timeout = 60_000)]
    public async Task A_canary_alert_is_still_sent_when_the_audit_cannot_record_anything()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using (IServiceScope scope = _h.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database
                .ExecuteSqlRawAsync("DROP TABLE PendingAuditEntries"); // as unwritable as a full disk
        }

        using SentinelMonitor monitor = Sentinel();
        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");
        await Eventually.HoldsAsync(() => Forwarded(canary), "the alert in the GRID queue");
        await Eventually.HoldsAsync(() => _h.Health.Failures >= 1, "the lost trace counted");
        await monitor.StopAsync(CancellationToken.None);

        _h.AuditLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains("CanaryAlert"));
    }
}
