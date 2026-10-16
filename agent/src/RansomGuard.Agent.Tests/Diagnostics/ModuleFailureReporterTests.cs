using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// The three traces of a module going down, and the per-minute limit on ModuleEventsLost
/// (CORRECTIF2_REVUE point 3).
/// </summary>
public sealed class ModuleFailureReporterTests
{
    private readonly InMemoryModuleStateRegistry _registry = new();
    private readonly RecordingLogger<ModuleFailureReporter> _log = new();
    private readonly ManualTimeProvider _time = new();
    private readonly List<(string Action, string Details)> _audit = [];
    private readonly ModuleFailureReporter _reporter;

    public ModuleFailureReporterTests()
    {
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(a => a.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string action, string details, string? _, Guid? _, CancellationToken _) =>
            {
                lock (_audit) _audit.Add((action, details));
            })
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => audit.Object);
        ServiceProvider provider = services.BuildServiceProvider();

        _reporter = new ModuleFailureReporter(
            _registry, provider.GetRequiredService<IServiceScopeFactory>(), _log, _time);
    }

    private List<(string Action, string Details)> EventsLostEntries(string module)
    {
        lock (_audit)
        {
            return _audit
                .Where(e => e.Action == ModuleFailureReporter.ModuleEventsLostAction)
                .Where(e => JsonDocument.Parse(e.Details).RootElement.GetProperty("module").GetString() == module)
                .ToList();
        }
    }

    private static int OverflowCount((string Action, string Details) entry) =>
        JsonDocument.Parse(entry.Details).RootElement.GetProperty("overflow_count").GetInt32();

    private int CriticalLogs() => _log.Entries.Count(e => e.Level == LogLevel.Critical);

    [Fact]
    public async Task A_stopped_module_leaves_registry_then_critical_log_then_audit_without_the_message()
    {
        var crash = new InvalidOperationException(@"secret path C:\Users\nurse\dossier.docx");

        await _reporter.ReportStoppedAsync(ModuleCode.Sentinel, reachedActive: true, crash);

        ModuleStateEntry state = _registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel);
        state.State.ShouldBe(ModuleState.Inactive);
        state.ReasonCode.ShouldBe(ModuleReasonCode.StoppedUnexpectedly);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Exception == crash);
        (string action, string details) = _audit.Single();
        action.ShouldBe(ModuleFailureReporter.ModuleStoppedAction);
        details.ShouldContain("System.InvalidOperationException");
        details.ShouldNotContain("secret path"); // the chain is uploaded: never the message
    }

    [Fact]
    public async Task A_module_failing_before_its_running_state_reads_init_failed()
    {
        await _reporter.ReportStoppedAsync(ModuleCode.Entropy, reachedActive: false, new IOException("disk"));

        _registry.Snapshot().Single(e => e.Code == ModuleCode.Entropy).ReasonCode.ShouldBe(ModuleReasonCode.InitFailed);
    }

    [Fact]
    public async Task A_burst_of_overflows_writes_one_entry_at_once_then_one_with_the_count_per_minute()
    {
        var overflow = new InternalBufferOverflowException();

        // 40 overflows within the first minute of an encryption burst.
        for (int i = 0; i < 40; i++)
        {
            await _reporter.ReportEventsLostAsync(ModuleCode.Entropy, overflow);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        // The first one is reported at once; the other 39 are only counted.
        EventsLostEntries(ModuleCode.Entropy).Count.ShouldBe(1);
        OverflowCount(EventsLostEntries(ModuleCode.Entropy)[0]).ShouldBe(1);
        CriticalLogs().ShouldBe(1);

        // The minute ends: one entry carries the 39 others.
        _time.Advance(TimeSpan.FromSeconds(20));
        await Eventually.HoldsAsync(() => EventsLostEntries(ModuleCode.Entropy).Count == 2, "the end-of-window entry");
        OverflowCount(EventsLostEntries(ModuleCode.Entropy)[1]).ShouldBe(39);
        CriticalLogs().ShouldBe(2);

        // A quiet minute writes nothing.
        _time.Advance(TimeSpan.FromSeconds(61));
        EventsLostEntries(ModuleCode.Entropy).Count.ShouldBe(2);

        // After the quiet period, the next loss is reported at once again.
        await _reporter.ReportEventsLostAsync(ModuleCode.Entropy, overflow);
        EventsLostEntries(ModuleCode.Entropy).Count.ShouldBe(3);
        OverflowCount(EventsLostEntries(ModuleCode.Entropy)[2]).ShouldBe(1);
    }

    [Fact]
    public async Task Never_more_than_one_entry_per_module_in_any_minute()
    {
        var overflow = new InternalBufferOverflowException();
        var written = new List<TimeSpan>();
        TimeSpan elapsed = TimeSpan.Zero;

        // An overflow every 2 s for 5 minutes.
        for (int i = 0; i < 150; i++)
        {
            // An entry written by the report itself is written now...
            int before = EventsLostEntries(ModuleCode.Sentinel).Count;
            await _reporter.ReportEventsLostAsync(ModuleCode.Sentinel, overflow);
            int afterReport = EventsLostEntries(ModuleCode.Sentinel).Count;
            written.AddRange(Enumerable.Repeat(elapsed, afterReport - before));

            // ...one written by the end-of-window timer, when the clock gets there.
            _time.Advance(TimeSpan.FromSeconds(2));
            elapsed += TimeSpan.FromSeconds(2);
            await Task.Yield();
            int afterTimer = EventsLostEntries(ModuleCode.Sentinel).Count;
            written.AddRange(Enumerable.Repeat(elapsed, afterTimer - afterReport));
        }

        written.Count.ShouldBe(6); // at 0, 60, 120, 180, 240, 300 s
        for (int i = 1; i < written.Count; i++)
        {
            (written[i] - written[i - 1]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(60));
        }

        // Nothing lost: the counts add up to every overflow reported so far.
        EventsLostEntries(ModuleCode.Sentinel).Sum(OverflowCount).ShouldBeLessThanOrEqualTo(150);
        _time.Advance(TimeSpan.FromSeconds(61));
        await Eventually.HoldsAsync(() => EventsLostEntries(ModuleCode.Sentinel).Sum(OverflowCount) == 150,
            "every overflow counted once the last window closes");
    }

    [Fact]
    public async Task Each_module_has_its_own_window()
    {
        var overflow = new InternalBufferOverflowException();

        await _reporter.ReportEventsLostAsync(ModuleCode.Sentinel, overflow);
        await _reporter.ReportEventsLostAsync(ModuleCode.Entropy, overflow);

        EventsLostEntries(ModuleCode.Sentinel).Count.ShouldBe(1);
        EventsLostEntries(ModuleCode.Entropy).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_overflow_leaves_the_module_state_unchanged()
    {
        _registry.Publish(ModuleCode.Entropy, ModuleState.Active);

        await _reporter.ReportEventsLostAsync(ModuleCode.Entropy, new InternalBufferOverflowException());

        _registry.Snapshot().Single(e => e.Code == ModuleCode.Entropy).State.ShouldBe(ModuleState.Active);
    }
}
