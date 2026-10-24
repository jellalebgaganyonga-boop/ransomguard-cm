using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Response;

/// <summary>ADR-026, 2c: the escalation timer. Nothing is resumed by itself; the alert repeats.</summary>
public sealed class SuspensionWatchServiceTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    private const int Writer = 4242;

    private readonly MonitorHarness _h = new();
    private readonly ManualTimeProvider _time = new();
    private readonly Dictionary<int, DateTime?> _running = new() { [Writer] = Created };

    public void Dispose() => _h.Dispose();

    private SuspensionWatchService Service(int escalationMinutes = 60) => new(
        _h.ScopeFactory, _h.AlertQueue,
        MonitorHarness.Config([], sentinel: new SentinelOptions
        {
            WatchDirectories = [_h.Folder], CanaryTemplates = ["dossier_patient"],
            Response = new SentinelResponseOptions { SuspendProcess = true, EscalationMinutes = escalationMinutes },
        }),
        NullLogger<SuspensionWatchService>.Instance, _time,
        readStartTime: pid => _running.TryGetValue(pid, out DateTime? at) ? at : null);

    private SuspendedProcess Suspend()
    {
        var record = new SuspendedProcess
        {
            ProcessId = Writer, ProcessCreatedAt = Created, ProcessName = "evil",
            CanaryPath = @"C:\Users\alice\Desktop\0001_x.txt", AlertId = Guid.NewGuid(),
            ClientMessageId = "rg-" + Guid.NewGuid().ToString("N"), SuspendedAt = _time.GetUtcNow().UtcDateTime,
        };
        using IServiceScope scope = _h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        db.SuspendedProcesses.Add(record);
        db.SaveChanges();
        return record;
    }

    private SuspendedProcess Reload(Guid id)
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AgentDbContext>().SuspendedProcesses.AsNoTracking().Single(s => s.Id == id);
    }

    private List<AlertIngestRequest> Alerts()
    {
        lock (_h.Forwarded) return _h.Forwarded.ToList();
    }

    [Fact]
    public async Task Without_a_decision_the_alert_repeats_at_every_escalation_delay_and_nothing_is_resumed()
    {
        SuspendedProcess record = Suspend();
        SuspensionWatchService service = Service(escalationMinutes: 60);

        _time.Advance(TimeSpan.FromMinutes(59));
        await service.SweepAsync(CancellationToken.None);
        Alerts().ShouldBeEmpty();

        _time.Advance(TimeSpan.FromMinutes(1));
        await service.SweepAsync(CancellationToken.None);
        AlertIngestRequest first = Alerts().ShouldHaveSingleItem();
        first.AlertType.ShouldBe("SentinelProcessSuspendedNoDecision");
        first.Details.Single(d => d.Key == "suspended_for_minutes").Value.ShouldBe("60");
        first.Details.Single(d => d.Key == "escalation").Value.ShouldBe("1");
        Reload(record.Id).State.ShouldBe(SuspensionState.Suspended); // still suspended: no automatic resume

        _time.Advance(TimeSpan.FromMinutes(30));
        await service.SweepAsync(CancellationToken.None);
        Alerts().Count.ShouldBe(1); // not before the next delay

        _time.Advance(TimeSpan.FromMinutes(30));
        await service.SweepAsync(CancellationToken.None);
        Alerts().Count.ShouldBe(2);
        Alerts()[1].ClientMessageId.ShouldNotBe(first.ClientMessageId); // each escalation is its own alert
        Reload(record.Id).EscalationCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_suspended_process_that_is_gone_is_closed_as_ended_and_never_escalated()
    {
        SuspendedProcess record = Suspend();
        _running.Remove(Writer); // the machine restarted, or someone killed it
        SuspensionWatchService service = Service();

        _time.Advance(TimeSpan.FromHours(2));
        await service.SweepAsync(CancellationToken.None);

        Reload(record.Id).State.ShouldBe(SuspensionState.Ended);
        // No escalation: there is nothing left to decide. One alert, and only one: the GRID is told
        // the suspension is closed, or the console would keep listing a process that is gone.
        Alerts().ShouldAllBe(a => a.AlertType == "SentinelSuspensionEnded");
        Alerts().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_PID_now_held_by_another_process_means_the_suspended_one_is_gone()
    {
        SuspendedProcess record = Suspend();
        _running[Writer] = Created.AddMinutes(3);

        await Service().SweepAsync(CancellationToken.None);

        Reload(record.Id).State.ShouldBe(SuspensionState.Ended);
    }
}
