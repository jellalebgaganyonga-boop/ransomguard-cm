using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel;

/// <summary>
/// V4 (plan de finition, J3 § 3.1): a modified or renamed canary stays watched. Its content is put
/// back, its hash taken again, and the next write on it is a new alert. Before, the first alert set
/// the status to Modified and the canary left the surveillance: a second write, 5 s later, raised
/// nothing.
/// </summary>
public sealed class SentinelCanaryRestoreTests : IDisposable
{
    private readonly MonitorHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SentinelMonitor Sentinel(int maxRestorationsPerHour = 10) => new(
        NullLogger<SentinelMonitor>.Instance,
        MonitorHarness.Config([], sentinel: new SentinelOptions
        {
            WatchDirectories = [_h.Folder],
            CanaryTemplates = ["dossier_patient"],
            CheckIntervalMs = 60_000, // the watcher only: the integrity check stays out of the count
            EnableRealTimeRegeneration = true,
            MaxRegenerationsPerHourPerDirectory = maxRestorationsPerHour,
        }),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        _h.AlertQueue,
        _h.Registry,
        _h.Reporter,
        startupDelay: TimeSpan.Zero);

    private async Task<SentinelMonitor> StartedAsync(int maxRestorationsPerHour = 10)
    {
        SentinelMonitor monitor = Sentinel(maxRestorationsPerHour);
        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");
        return monitor;
    }

    private int Alerts(SentinelCanary canary, string type = "SentinelCanaryModified")
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.Count(a => a.AlertType == type && Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }

    private SentinelCanary Reload(Guid id)
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return db.SentinelCanaries.AsNoTracking().Single(c => c.Id == id);
    }

    private async Task<bool> IntactAsync(SentinelCanary canary)
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CanaryFileService>().VerifyCanaryIntegrityAsync(Reload(canary.Id));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_modified_canary_is_restored_and_a_second_write_5_s_later_is_detected_too()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        string originalHash = canary.OriginalContentHash;
        using SentinelMonitor monitor = await StartedAsync();

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");
        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the first alert");

        // Restored: content put back from the template, hash taken again, still Active.
        await Eventually.HoldsAsync(() => Reload(canary.Id).OriginalContentHash != originalHash, "the hash taken again");
        SentinelCanary restored = Reload(canary.Id);
        restored.Status.ShouldBe(CanaryStatus.Active);
        (await IntactAsync(canary)).ShouldBeTrue();
        (File.GetAttributes(canary.FilePath) & FileAttributes.Hidden).ShouldBe(FileAttributes.Hidden);

        // The restoration itself is the agent's own write: no alert for it.
        await Task.Delay(5_000);
        Alerts(canary).ShouldBe(1);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED AGAIN");
        await Eventually.HoldsAsync(() => Alerts(canary) == 2, "the second alert, 5 s after the first");
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_renamed_canary_is_recreated_at_its_path_and_stays_watched()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedAsync();

        string renamed = canary.FilePath + ".locked";
        File.Move(canary.FilePath, renamed);
        await Eventually.HoldsAsync(() => Alerts(canary, "SentinelCanaryRenamed") == 1, "the rename alert");

        await Eventually.HoldsAsync(() => File.Exists(canary.FilePath), "the canary recreated at its own path");
        Reload(canary.Id).Status.ShouldBe(CanaryStatus.Active);
        File.Exists(renamed).ShouldBeTrue(); // the attacker's copy is left alone: nothing in user space is deleted

        await Task.Delay(3_500);
        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");
        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "a write on the recreated canary is detected");
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task Past_the_hourly_limit_the_canary_leaves_the_surveillance_and_SustainedAttack_is_recorded()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedAsync(maxRestorationsPerHour: 1);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");
        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the first alert");
        await Eventually.HoldsAsync(() => _h.ChainedActions().Concat(PendingActions()).Contains("CanaryRestored"), "the first restoration");

        await Task.Delay(3_500);
        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED AGAIN");
        await Eventually.HoldsAsync(() => Alerts(canary) == 2, "the second alert");
        await Eventually.HoldsAsync(() => Reload(canary.Id).Status == CanaryStatus.Modified, "the canary leaves the surveillance past the limit");
        // Awaited like the checks above: TryRestoreCanaryAsync records the status, then appends
        // SustainedAttack, two writes in sequence. Asserted at once, a slow runner could land between
        // them (CI run 40122324735: ["CanaryAlert", "CanaryRestored", "CanaryAlert"]).
        await Eventually.HoldsAsync(() => _h.ChainedActions().Concat(PendingActions()).Contains("SustainedAttack"), "SustainedAttack recorded");

        // Out of the surveillance, said as such: a third write raises nothing.
        await Task.Delay(3_500);
        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED ONCE MORE");
        await Task.Delay(2_000);
        Alerts(canary).ShouldBe(2);
        await monitor.StopAsync(CancellationToken.None);
    }

    private List<string> PendingActions()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AgentDbContext>().PendingAuditEntries.Select(e => e.Action).ToList();
    }
}
