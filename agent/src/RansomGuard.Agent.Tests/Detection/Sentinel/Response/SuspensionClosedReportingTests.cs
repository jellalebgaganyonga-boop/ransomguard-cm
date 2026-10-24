using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Response;

/// <summary>
/// A suspension that stops being open must reach the GRID (ADR-026). The agent used to tell it
/// only that a process had been suspended: a resume, or a process that exited by itself, stayed a
/// local row and an audit line. The console, which knows the endpoint only through what the agent
/// sends, would go on listing a frozen process that is running again or gone — an action claimed
/// after it stopped being true. Each closure is now a response alert linked to the same canary
/// alert by <c>response_to</c>.
/// </summary>
public sealed class SuspensionClosedReportingTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
    private const int Writer = 4242;
    private const string Canary = @"C:\Users\alice\Documents\0001_dossier_patient_ab12cd34.txt";

    private readonly MonitorHarness _h = new();
    private readonly FakeSuspender _suspender = new();
    private readonly ManualTimeProvider _time = new();
    private readonly Dictionary<int, DateTime?> _running = new() { [Writer] = Created };
    private readonly string _clientMessageId = "rg-" + Guid.NewGuid().ToString("N");

    public void Dispose() => _h.Dispose();

    private CanaryResponseEngine Engine() => new(
        _suspender, _h.ScopeFactory, _h.AlertQueue,
        new SentinelResponseOptions { SuspendProcess = true, EscalationMinutes = 60 },
        NullLogger<CanaryResponseEngine>.Instance, _time,
        readStartTime: pid => _running.TryGetValue(pid, out DateTime? at) ? at : null,
        agentPid: 1,
        resumeRetryDelay: TimeSpan.Zero);

    private async Task<Guid> SuspendAsync(CanaryResponseEngine engine)
    {
        ResponseOutcome outcome = await engine.RespondAsync(
            new AttributionOutcome(AttributionState.Resolved, Writer, Created, "evil", @"C:\Temp\evil.exe"),
            Canary, Guid.NewGuid(), _clientMessageId);
        outcome.Suspended.ShouldBeTrue();
        return outcome.SuspensionId!.Value;
    }

    private List<AlertIngestRequest> Forwarded()
    {
        lock (_h.Forwarded) return _h.Forwarded.ToList();
    }

    private static string? Detail(AlertIngestRequest alert, string key) =>
        alert.Details?.FirstOrDefault(d => d.Key == key)?.Value;

    [Fact]
    public async Task A_resume_is_reported_so_the_console_stops_showing_the_process_as_suspended()
    {
        CanaryResponseEngine engine = Engine();
        Guid suspension = await SuspendAsync(engine);

        (await engine.ResumeAsync(suspension, "command cmd-1")).ShouldBeTrue();

        AlertIngestRequest closed = Forwarded().Where(a => a.AlertType == "SentinelProcessResumed").ShouldHaveSingleItem();
        Detail(closed, "response").ShouldBe("process_resumed");
        Detail(closed, "suspension_id").ShouldBe(suspension.ToString());
        Detail(closed, "response_to").ShouldBe(_clientMessageId); // same canary alert as the suspension
        Detail(closed, "closed_by").ShouldBe("command cmd-1");
        closed.Severity.ShouldBe("Medium");
    }

    [Fact]
    public async Task A_process_gone_before_the_resume_is_reported_as_ended_not_as_resumed()
    {
        CanaryResponseEngine engine = Engine();
        Guid suspension = await SuspendAsync(engine);
        _running[Writer] = null; // the writer exited on its own

        (await engine.ResumeAsync(suspension, "command cmd-2")).ShouldBeTrue();

        AlertIngestRequest closed = Forwarded().Where(a => a.AlertType == "SentinelSuspensionEnded").ShouldHaveSingleItem();
        Detail(closed, "response").ShouldBe("suspension_ended");
        Detail(closed, "suspension_id").ShouldBe(suspension.ToString());
        Forwarded().ShouldNotContain(a => a.AlertType == "SentinelProcessResumed"); // nothing was resumed
    }

    [Fact]
    public async Task A_resume_that_fails_is_reported_critical_and_says_the_process_stays_suspended()
    {
        CanaryResponseEngine engine = Engine();
        Guid suspension = await SuspendAsync(engine);
        _suspender.ResumeOutcome = SuspendOutcome.AccessDenied;

        (await engine.ResumeAsync(suspension, "command cmd-3")).ShouldBeFalse();

        AlertIngestRequest closed = Forwarded().Where(a => a.AlertType == "SentinelProcessResumeFailed").ShouldHaveSingleItem();
        Detail(closed, "response").ShouldBe("resume_failed");
        closed.Severity.ShouldBe("Critical"); // a process frozen with no way back
        closed.Summary.ShouldContain("stays suspended");
    }

    [Fact]
    public async Task Each_closure_carries_the_suspension_it_closes_so_the_console_can_match_them()
    {
        CanaryResponseEngine engine = Engine();
        Guid suspension = await SuspendAsync(engine);

        AlertIngestRequest opened = Forwarded().Where(a => a.AlertType == "SentinelProcessSuspended").ShouldHaveSingleItem();
        await engine.ResumeAsync(suspension, "command cmd-4");
        AlertIngestRequest closed = Forwarded().Where(a => a.AlertType == "SentinelProcessResumed").ShouldHaveSingleItem();

        Detail(opened, "suspension_id").ShouldBe(Detail(closed, "suspension_id"));
        opened.ClientMessageId.ShouldNotBe(closed.ClientMessageId); // two alerts, not one overwritten
    }

    [Fact]
    public async Task The_row_and_the_alert_agree_on_the_state()
    {
        CanaryResponseEngine engine = Engine();
        Guid suspension = await SuspendAsync(engine);

        await engine.ResumeAsync(suspension, "command cmd-5");

        using IServiceScope scope = _h.Provider.CreateScope();
        SuspendedProcess row = scope.ServiceProvider.GetRequiredService<AgentDbContext>()
            .SuspendedProcesses.AsNoTracking().Single(s => s.Id == suspension);
        row.State.ShouldBe(SuspensionState.Resumed);
        Forwarded().ShouldContain(a => a.AlertType == "SentinelProcessResumed");
    }
}
