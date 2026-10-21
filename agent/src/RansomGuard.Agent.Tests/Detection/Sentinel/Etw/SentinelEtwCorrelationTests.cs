using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// SENTINEL with the ETW session (ADR-025, commit 3), on a fake kernel trace: a write only ETW sees
/// is detected; a write both sources see is one alert, not two.
/// </summary>
public sealed class SentinelEtwCorrelationTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly EmittingTrace _trace = new();

    // The processes the attribution may find running: 4242 and 5151, both started before any write.
    private readonly ProcessCreationTimes _processes = new(pid => pid switch
    {
        4242 => new ProcessFacts(DateTime.UtcNow.AddHours(-1), "evil", @"C:\Users\x\AppData\Local\Temp\evil.exe"),
        5151 => new ProcessFacts(DateTime.UtcNow.AddHours(-2), "other", @"C:\Tools\other.exe"),
        _ => null,
    });
    private int _tracesOpened;

    public void Dispose() => _h.Dispose();

    [Fact(Timeout = 30_000)]
    public async Task A_write_only_ETW_sees_raises_one_alert()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        // A rewrite through a mapped view: the watcher sees nothing, ETW sees the write.
        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, 4242, DateTime.UtcNow, 0, 0));

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert raised by ETW alone");
        await Task.Delay(2500); // past the correlation window
        Alerts(canary).ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_write_both_sources_see_is_one_alert_not_two()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED"); // the watcher fires
        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, 4242, DateTime.UtcNow, 0, 0));

        await Eventually.HoldsAsync(() => Alerts(canary) >= 1, "the alert");
        await Task.Delay(2500);
        Alerts(canary).ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task ETW_reporting_the_canary_with_another_case_still_finds_it()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath.ToUpperInvariant(), 4242, DateTime.UtcNow, 0, 0));

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert, path matched case-insensitively");
    }

    [Fact(Timeout = 30_000)]
    public async Task With_ETW_disabled_no_session_is_opened_and_the_watcher_still_detects()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: false);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the watcher's alert");
        _tracesOpened.ShouldBe(0);
    }

    private async Task<SentinelMonitor> StartedSentinelAsync(bool etwEnabled)
    {
        var session = new EtwFileIoSession(
            () =>
            {
                Interlocked.Increment(ref _tracesOpened);
                return _trace;
            },
            NullLogger<EtwFileIoSession>.Instance,
            activeSessions: () => []);
        var monitor = new SentinelMonitor(
            NullLogger<SentinelMonitor>.Instance,
            MonitorHarness.Config([], sentinel: new SentinelOptions
            {
                WatchDirectories = [_h.Folder],
                CanaryTemplates = ["dossier_patient"],
                CheckIntervalMs = 60_000, // the integrity check stays out of the count
                EnableRealTimeRegeneration = false,
                EtwEnabled = etwEnabled,
            }),
            _h.ScopeFactory,
            new FileEventDeduplicator(50),
            _h.AlertQueue,
            _h.Registry,
            _h.Reporter,
            startupDelay: TimeSpan.Zero,
            etwSession: session,
            processTimes: _processes);
        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");
        if (etwEnabled)
        {
            _trace.Processing.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the ETW session never started processing");
        }

        return monitor;
    }

    [Fact(Timeout = 30_000)]
    public async Task The_alert_leaves_pending_and_its_complement_names_the_writer()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, 4242, DateTime.UtcNow, 0, 0));

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert");
        AlertIngestRequest alert = Alert(canary);
        Detail(alert, "attribution_state").ShouldBe("pending");
        alert.Details.ShouldNotContain(d => d.Key == "process_id"); // the alert never waits for it
        await Eventually.HoldsAsync(() => Complements(alert).Count == 1, "the attribution complement");
        AttributionComplementRequest complement = Complements(alert)[0];
        complement.State.ShouldBe("resolved");
        complement.Pid.ShouldBe(4242);
        complement.ProcessName.ShouldBe("evil");
        complement.ProcessCreatedAt.ShouldNotBeNull();
        complement.Reason.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_write_only_the_watcher_saw_while_ETW_runs_is_attributed_to_nobody_no_write_event()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED"); // no ETW record: the session missed it

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert");
        AlertIngestRequest alert = Alert(canary);
        await Eventually.HoldsAsync(() => Complements(alert).Count == 1, "the attribution complement");
        Complements(alert)[0].State.ShouldBe("unavailable");
        Complements(alert)[0].Reason.ShouldBe("no_write_event");
        Complements(alert)[0].Pid.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Two_writers_in_the_window_are_ambiguous()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: true);

        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, 4242, DateTime.UtcNow, 0, 0));
        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, 5151, DateTime.UtcNow, 0, 0));

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert");
        AlertIngestRequest alert = Alert(canary);
        await Eventually.HoldsAsync(() => Complements(alert).Count == 1, "the attribution complement");
        Complements(alert)[0].State.ShouldBe("ambiguous");
        Complements(alert)[0].Pid.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Without_ETW_the_alert_is_final_etw_unavailable_and_no_complement_follows()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        using SentinelMonitor monitor = await StartedSentinelAsync(etwEnabled: false);

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the watcher's alert");
        AlertIngestRequest alert = Alert(canary);
        Detail(alert, "attribution_state").ShouldBe("unavailable");
        Detail(alert, "attribution_reason").ShouldBe("etw_unavailable");
        await Task.Delay(2000); // past the attribution delay
        Complements(alert).ShouldBeEmpty();
    }

    private AlertIngestRequest Alert(SentinelCanary canary)
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.Single(a => Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }

    private List<AttributionComplementRequest> Complements(AlertIngestRequest alert)
    {
        lock (_h.Attributions)
        {
            return _h.Attributions.Where(a => a.ClientMessageId == alert.ClientMessageId).Select(a => a.Complement).ToList();
        }
    }

    private static string? Detail(AlertIngestRequest alert, string key) =>
        alert.Details.SingleOrDefault(d => d.Key == key)?.Value;

    private int Alerts(SentinelCanary canary)
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.Count(a => a.AlertType == "SentinelCanaryModified"
                && Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }

    /// <summary>A kernel trace the test feeds by hand.</summary>
    private sealed class EmittingTrace : IKernelFileIoTrace
    {
        private readonly ManualResetEventSlim _stop = new();
        private Action<FileIoRecord>? _onFileIo;

        public ManualResetEventSlim Processing { get; } = new();

        public int? EventsLost => 0;

        public void Open(string sessionName, int bufferSizeMb)
        {
        }

        public void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
        {
            _onFileIo = onFileIo;
            Processing.Set();
            _stop.Wait();
        }

        public void Emit(FileIoRecord record) => _onFileIo!(record);

        public void Flush()
        {
        }

        public void Stop() => _stop.Set();

        public void Dispose()
        {
        }
    }
}
