using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Response;

/// <summary>The kernel calls, recorded; the outcome is chosen by the test.</summary>
internal sealed class FakeSuspender : IProcessSuspender
{
    public List<int> Suspended { get; } = [];

    public List<int> Resumed { get; } = [];

    public SuspendOutcome SuspendOutcome { get; set; } = SuspendOutcome.Done;

    public SuspendOutcome ResumeOutcome { get; set; } = SuspendOutcome.Done;

    public SuspendOutcome Suspend(int processId, out string? error)
    {
        lock (Suspended) Suspended.Add(processId);
        error = SuspendOutcome == SuspendOutcome.Done ? null : "fake";
        return SuspendOutcome;
    }

    public SuspendOutcome Resume(int processId, out string? error)
    {
        lock (Resumed) Resumed.Add(processId);
        error = ResumeOutcome == SuspendOutcome.Done ? null : "fake";
        return ResumeOutcome;
    }
}

/// <summary>
/// ADR-026, 2a: the engine suspends on a resolved attribution only, after re-reading the (PID,
/// creation time) pair, never PID 0/4, never the agent; it never acts on a guess.
/// </summary>
public sealed class CanaryResponseEngineTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    private const int Writer = 4242;
    private const string Canary = @"C:\Users\alice\Documents\0001_dossier_patient_ab12cd34.txt";

    private readonly MonitorHarness _h = new();
    private readonly FakeSuspender _suspender = new();
    private readonly ManualTimeProvider _time = new();
    private readonly Dictionary<int, DateTime?> _running = new() { [Writer] = Created };
    private readonly Guid _alertId = Guid.NewGuid();
    private readonly string _clientMessageId = "rg-" + Guid.NewGuid().ToString("N");

    public void Dispose() => _h.Dispose();

    private CanaryResponseEngine Engine(bool enabled = true, int agentPid = 1) => new(
        _suspender, _h.ScopeFactory, _h.AlertQueue,
        new SentinelResponseOptions { SuspendProcess = enabled, EscalationMinutes = 60 },
        NullLogger<CanaryResponseEngine>.Instance, _time,
        readStartTime: pid => _running.TryGetValue(pid, out DateTime? at) ? at : null,
        agentPid: agentPid,
        resumeRetryDelay: TimeSpan.Zero);

    private static AttributionOutcome Resolved(int pid = Writer, DateTime? created = null) =>
        new(AttributionState.Resolved, pid, created ?? Created, "evil", @"C:\Temp\evil.exe");

    private Task<ResponseOutcome> RespondAsync(CanaryResponseEngine engine, AttributionOutcome attribution, string canary = Canary) =>
        engine.RespondAsync(attribution, canary, _alertId, _clientMessageId);

    private List<string> AuditActions()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return db.PendingAuditEntries.Select(e => e.Action).Concat(db.AuditLogs.Select(a => a.Action)).ToList();
    }

    private SuspendedProcess? Record()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AgentDbContext>().SuspendedProcesses.AsNoTracking().SingleOrDefault();
    }

    private List<AlertIngestRequest> Alerts()
    {
        lock (_h.Forwarded) return _h.Forwarded.ToList();
    }

    [Fact]
    public async Task A_resolved_writer_is_suspended_recorded_audited_and_the_console_told()
    {
        ResponseOutcome outcome = await RespondAsync(Engine(), Resolved());

        outcome.Suspended.ShouldBeTrue();
        _suspender.Suspended.ShouldBe([Writer]);
        SuspendedProcess record = Record().ShouldNotBeNull();
        record.ProcessId.ShouldBe(Writer);
        record.ProcessCreatedAt.ShouldBe(Created);
        record.State.ShouldBe(SuspensionState.Suspended);
        record.ClientMessageId.ShouldBe(_clientMessageId);
        record.Id.ShouldBe(outcome.SuspensionId!.Value);
        AuditActions().ShouldBe(["ProcessSuspendIntent", "ProcessSuspended"]); // the intent before the action
        AlertIngestRequest alert = Alerts().ShouldHaveSingleItem();
        alert.AlertType.ShouldBe("SentinelProcessSuspended");
        alert.Severity.ShouldBe("Critical");
        alert.Details.Single(d => d.Key == "response_to").Value.ShouldBe(_clientMessageId);
        alert.Details.Single(d => d.Key == "process_id").Value.ShouldBe("4242");
    }

    [Theory]
    [InlineData(AttributionState.Unavailable)]
    [InlineData(AttributionState.Ambiguous)]
    public async Task Nobody_is_suspended_without_a_resolved_attribution(AttributionState state)
    {
        ResponseOutcome outcome = await RespondAsync(Engine(), new AttributionOutcome(state, Reason: "no_write_event"));

        outcome.Suspended.ShouldBeFalse();
        outcome.Reason.ShouldBe(ResponseReason.NotResolved);
        _suspender.Suspended.ShouldBeEmpty();
        Record().ShouldBeNull();
        Alerts().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_agent_itself_is_never_suspended()
    {
        ResponseOutcome outcome = await RespondAsync(Engine(agentPid: Writer), Resolved());

        outcome.Reason.ShouldBe(ResponseReason.AgentPid);
        _suspender.Suspended.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task The_system_is_never_suspended(int pid)
    {
        _running[pid] = Created;

        ResponseOutcome outcome = await RespondAsync(Engine(), Resolved(pid));

        outcome.Reason.ShouldBe(ResponseReason.SystemPid);
        _suspender.Suspended.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_PID_now_born_at_another_time_is_reused_and_not_suspended()
    {
        _running[Writer] = Created.AddSeconds(1); // another process holds the PID now

        ResponseOutcome outcome = await RespondAsync(Engine(), Resolved());

        outcome.Reason.ShouldBe(ResponseReason.PidReused);
        _suspender.Suspended.ShouldBeEmpty();
        AuditActions().ShouldBeEmpty(); // no intent: nothing was going to be done
    }

    [Fact]
    public async Task A_creation_time_within_the_tolerance_is_the_same_process()
    {
        _running[Writer] = Created.AddMilliseconds(40);

        (await RespondAsync(Engine(), Resolved())).Suspended.ShouldBeTrue();
    }

    [Fact]
    public async Task A_writer_gone_before_the_response_is_not_suspended()
    {
        _running.Remove(Writer);

        ResponseOutcome outcome = await RespondAsync(Engine(), Resolved());

        outcome.Reason.ShouldBe(ResponseReason.ProcessExited);
        _suspender.Suspended.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_suspension_the_kernel_refuses_is_audited_and_alerted_and_nothing_is_recorded_as_suspended()
    {
        _suspender.SuspendOutcome = SuspendOutcome.AccessDenied;

        ResponseOutcome outcome = await RespondAsync(Engine(), Resolved());

        outcome.Suspended.ShouldBeFalse();
        outcome.Reason.ShouldBe(ResponseReason.AccessDenied);
        Record().ShouldBeNull();
        AuditActions().ShouldBe(["ProcessSuspendIntent", "ProcessSuspendFailed"]);
        AlertIngestRequest alert = Alerts().ShouldHaveSingleItem();
        alert.AlertType.ShouldBe("SentinelProcessSuspendFailed");
        alert.Details.Single(d => d.Key == "reason").Value.ShouldBe("access_denied");
    }

    [Fact]
    public async Task Disabled_by_configuration_the_engine_never_acts()
    {
        ResponseOutcome outcome = await RespondAsync(Engine(enabled: false), Resolved());

        outcome.Reason.ShouldBe(ResponseReason.DisabledByConfig);
        _suspender.Suspended.ShouldBeEmpty();
        Alerts().ShouldBeEmpty();
    }

    [Fact]
    public async Task Three_unattributed_canaries_in_a_minute_recommend_isolation_once()
    {
        CanaryResponseEngine engine = Engine();
        var unavailable = new AttributionOutcome(AttributionState.Unavailable, Reason: "no_write_event");

        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_a.txt");
        _time.Advance(TimeSpan.FromSeconds(10));
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_b.txt");
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_b.txt"); // the same canary twice counts once
        Alerts().ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(10));
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_c.txt");
        AlertIngestRequest alert = Alerts().ShouldHaveSingleItem();
        alert.AlertType.ShouldBe("SentinelIsolationRecommended");
        alert.Details.Single(d => d.Key == "canaries").Value.ShouldBe("3");
        AuditActions().ShouldContain("IsolationRecommended");

        // A fourth within the window does not repeat the recommendation; past the window it does.
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_d.txt");
        Alerts().Count.ShouldBe(1);
        _time.Advance(TimeSpan.FromSeconds(61));
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_e.txt");
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_f.txt");
        await RespondAsync(engine, unavailable, @"C:\Users\alice\Desktop\0001_g.txt");
        Alerts().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_suspended_process_is_resumed_on_request_and_the_record_says_by_whom()
    {
        CanaryResponseEngine engine = Engine();
        ResponseOutcome outcome = await RespondAsync(engine, Resolved());

        (await engine.ResumeAsync(outcome.SuspensionId!.Value, "command 7f3a")).ShouldBeTrue();

        _suspender.Resumed.ShouldBe([Writer]);
        SuspendedProcess record = Record()!;
        record.State.ShouldBe(SuspensionState.Resumed);
        record.ResumedBy.ShouldBe("command 7f3a");
        AuditActions().ShouldBe(["ProcessSuspendIntent", "ProcessSuspended", "ProcessResumeIntent", "ProcessResumed"]);
    }

    [Fact]
    public async Task A_resume_asked_for_a_PID_now_someone_elses_touches_nobody_and_closes_the_record()
    {
        CanaryResponseEngine engine = Engine();
        ResponseOutcome outcome = await RespondAsync(engine, Resolved());
        _running[Writer] = Created.AddMinutes(5); // the writer died, the PID was handed to another process

        (await engine.ResumeAsync(outcome.SuspensionId!.Value, "operator")).ShouldBeTrue();

        _suspender.Resumed.ShouldBeEmpty();
        Record()!.State.ShouldBe(SuspensionState.Ended);
    }

    [Fact]
    public async Task A_resume_that_fails_three_times_leaves_the_record_ResumeFailed_and_says_so()
    {
        CanaryResponseEngine engine = Engine();
        ResponseOutcome outcome = await RespondAsync(engine, Resolved());
        _suspender.ResumeOutcome = SuspendOutcome.Failed;

        (await engine.ResumeAsync(outcome.SuspensionId!.Value, "operator")).ShouldBeFalse();

        _suspender.Resumed.Count.ShouldBe(CanaryResponseEngine.ResumeAttempts);
        Record()!.State.ShouldBe(SuspensionState.ResumeFailed);
        AuditActions().ShouldContain("ProcessResumeFailed");
    }
}
