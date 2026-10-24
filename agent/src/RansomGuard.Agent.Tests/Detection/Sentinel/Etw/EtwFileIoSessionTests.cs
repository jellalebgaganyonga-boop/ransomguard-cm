using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The SENTINEL ETW FileIO session (ADR-025, commit 1): its lifecycle, its flush cadence, its
/// failure reasons, the stop from outside and the restarts, on fake kernel traces and a manual
/// clock. The real session is exercised by <see cref="EtwFileIoSessionLiveTests"/>.
/// </summary>
public sealed class EtwFileIoSessionTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly List<FakeTrace> _traces = [];
    private readonly RecordingLogger<EtwFileIoSession> _log = new();
    private readonly List<FileIoRecord> _records = [];
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly List<string> _activeSessions = ["EventLog-System", "SYSMON TRACE"];
    private readonly EtwFileIoSession _session;
    private Exception? _openThrows;

    public EtwFileIoSessionTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _audit.Object);
        ServiceProvider provider = services.BuildServiceProvider();

        _session = new EtwFileIoSession(
            () =>
            {
                var trace = new FakeTrace { OpenThrows = _openThrows };
                _traces.Add(trace);
                return trace;
            },
            _log, _time, () => _activeSessions, provider.GetRequiredService<IServiceScopeFactory>());
    }

    private FakeTrace Trace => _traces[^1];

    public void Dispose() => _session.Dispose();

    [Fact]
    public void It_opens_its_own_named_session_with_64_MB_of_buffers()
    {
        StartAndWait().ShouldBeTrue();

        Trace.OpenedName.ShouldBe("RansomGuard-Sentinel-FileIO");
        Trace.OpenedName!.ShouldNotContain("ExfilWatch"); // separate from EXFIL WATCH's session
        Trace.OpenedBufferMb.ShouldBe(64);
        _session.State.ShouldBe(EtwSessionState.Running);
        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.None);
        _log.Entries.ShouldNotContain(e => e.Message.Contains("etw_orphan_session_replaced"));
    }

    [Fact]
    public void It_flushes_every_100_ms_and_not_in_between()
    {
        StartAndWait();

        _time.Advance(TimeSpan.FromMilliseconds(99));
        Trace.Flushes.ShouldBe(0);
        for (int i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Trace.Flushes.ShouldBe(10);
        _session.Flushes.ShouldBe(10);
    }

    [Fact]
    public void The_records_reach_the_subscriber()
    {
        StartAndWait();
        var record = new FileIoRecord(FileIoOperation.Write, @"C:\canaries\a.txt", 4321, DateTime.UtcNow, 0, 0);

        Trace.Emit(record);

        _records.ShouldBe([record]);
    }

    [Fact]
    public void Without_administrator_rights_it_is_unavailable_says_why_and_flushes_nothing()
    {
        _openThrows = new UnauthorizedAccessException("Access Denied (Administrator rights required to start ETW)");

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.State.ShouldBe(EtwSessionState.Unavailable);
        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.AdminRightsMissing);
        _time.ActiveTimers.ShouldBe(0); // no flush, and no restart: rights do not come back by waiting
        Trace.Disposed.ShouldBeTrue();
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("etw_admin_rights_missing"));
    }

    [Fact]
    public void With_no_session_slot_left_it_says_so_distinctly_and_lists_the_active_sessions()
    {
        _openThrows = new COMException("Insufficient system resources exist", unchecked((int)0x800705AA));

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.NoSessionSlot);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("etw_no_session_slot") && e.Message.Contains("SYSMON TRACE"));
        _log.Entries.ShouldNotContain(e => e.Message.Contains("etw_admin_rights_missing"));
    }

    [Fact]
    public void Any_other_failure_to_open_is_a_start_failure_logged_as_an_error()
    {
        _openThrows = new COMException("The instance name passed was not recognized", unchecked((int)0x80071069));

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.StartFailed);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("etw_start_failed"));
    }

    [Fact]
    public void A_session_left_by_a_killed_agent_is_announced_with_a_warning_and_replaced()
    {
        _activeSessions.Add("RansomGuard-Sentinel-FileIO");

        StartAndWait().ShouldBeTrue();

        _log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("etw_orphan_session_replaced"));
        _session.State.ShouldBe(EtwSessionState.Running);
    }

    [Fact]
    public void Stopped_from_outside_it_says_so_critically_audits_it_and_stops_flushing()
    {
        StartAndWait();

        Trace.EndFromOutside(); // logman stop RansomGuard-Sentinel-FileIO -ets: Process() returns
        WaitUntilUnavailableWithRestartScheduled();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.StoppedExternally);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains("etw_session_stopped_externally"));
        Eventually(() => _audit.Invocations.Count > 0);
        _audit.Verify(a => a.AppendAsync(
            "EtwSessionStoppedExternally",
            It.Is<string>(d => d.Contains("\"reason\":\"etw_session_stopped_externally\"") && d.Contains("\"restart_in_seconds\":30")),
            "Module", null, It.IsAny<CancellationToken>()), Times.Once);
        int flushes = Trace.Flushes;
        _time.Advance(TimeSpan.FromMilliseconds(500));
        Trace.Flushes.ShouldBe(flushes); // the flush timer is gone
    }

    [Fact]
    public void After_a_stop_from_outside_it_restarts_after_30_s_and_is_running_again()
    {
        StartAndWait();
        Trace.EndFromOutside();
        WaitUntilUnavailableWithRestartScheduled();

        _time.Advance(TimeSpan.FromSeconds(29));
        _traces.Count.ShouldBe(1);
        _time.Advance(TimeSpan.FromSeconds(1));

        _traces.Count.ShouldBe(2);
        Trace.Processing.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        _session.State.ShouldBe(EtwSessionState.Running);
        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.None);
        _log.Entries.ShouldContain(e => e.Message.Contains("restart attempt 1 in"));
        _log.Entries.ShouldContain(e => e.Message.Contains("session restarted (attempt 1)"));
        Trace.Emit(new FileIoRecord(FileIoOperation.Write, @"C:\canaries\b.txt", 99, DateTime.UtcNow, 0, 0));
        _records.ShouldHaveSingleItem(); // the new session delivers to the same subscriber
    }

    [Fact]
    public void Failed_restarts_back_off_30_s_then_2_min_then_every_10_min_each_attempt_logged()
    {
        StartAndWait();
        _openThrows = new COMException("Insufficient system resources exist", unchecked((int)0x800705AA));
        Trace.EndFromOutside();
        WaitUntilUnavailableWithRestartScheduled();

        _time.Advance(TimeSpan.FromSeconds(30));
        _traces.Count.ShouldBe(2); // attempt 1, at 30 s
        _time.Advance(TimeSpan.FromSeconds(119));
        _traces.Count.ShouldBe(2);
        _time.Advance(TimeSpan.FromSeconds(1));
        _traces.Count.ShouldBe(3); // attempt 2, 2 min later
        _time.Advance(TimeSpan.FromMinutes(10));
        _traces.Count.ShouldBe(4); // attempt 3, 10 min later
        _time.Advance(TimeSpan.FromMinutes(10));
        _traces.Count.ShouldBe(5); // and every 10 min after that

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.NoSessionSlot);
        _session.RestartAttempts.ShouldBe(4);
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            _log.Entries.ShouldContain(e => e.Message.Contains($"restart attempt {attempt} failed"));
        }
    }

    [Fact]
    public void A_stop_asked_by_the_agent_cancels_a_pending_restart()
    {
        StartAndWait();
        Trace.EndFromOutside();
        WaitUntilUnavailableWithRestartScheduled();

        _session.Stop();
        _time.Advance(TimeSpan.FromMinutes(30));

        _traces.Count.ShouldBe(1);
        _time.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public void A_failure_while_processing_makes_it_unavailable_and_schedules_a_restart()
    {
        StartAndWait();

        Trace.FailProcessing(new InvalidOperationException("real-time buffers lost"));
        WaitUntilUnavailableWithRestartScheduled();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.ProcessingFailed);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("etw_processing_failed"));
        _audit.Invocations.ShouldBeEmpty(); // a failure, not a stop from outside
        _time.Advance(TimeSpan.FromSeconds(30));
        _traces.Count.ShouldBe(2);
    }

    [Fact]
    public void Stop_stops_the_trace_waits_for_its_thread_and_flushes_no_more()
    {
        StartAndWait();

        _session.Stop();
        _time.Advance(TimeSpan.FromSeconds(1));

        _session.State.ShouldBe(EtwSessionState.Stopped);
        Trace.Stopped.ShouldBeTrue();
        Trace.Disposed.ShouldBeTrue();
        Trace.ProcessReturned.ShouldBeTrue();
        Trace.Flushes.ShouldBe(0);
        _time.ActiveTimers.ShouldBe(0);
        _log.Entries.ShouldNotContain(e => e.Level == LogLevel.Critical); // an asked stop is not an attack
        _audit.Invocations.ShouldBeEmpty();
        Should.NotThrow(_session.Stop); // idempotent
    }

    [Fact]
    public void Events_lost_is_null_when_unknown_never_zero()
    {
        _session.EventsLost.ShouldBeNull(); // not started

        StartAndWait();
        Trace.Lost = null;
        _session.EventsLost.ShouldBeNull();
        Trace.Lost = 12;
        _session.EventsLost.ShouldBe(12);
    }

    private bool StartAndWait()
    {
        bool started = _session.Start(_records.Add, _ => { });
        if (started)
        {
            Trace.Processing.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the processing thread never called Process");
        }

        return started;
    }

    // The session publishes Unavailable, then schedules its restart, on its own thread: wait for
    // both before moving the clock, or the restart timer would be created after the move.
    private void WaitUntilUnavailableWithRestartScheduled() =>
        Eventually(() => _session.State == EtwSessionState.Unavailable && _time.ActiveTimers == 1);

    private static void Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not reached within 5 s");
            Thread.Sleep(5);
        }
    }

    /// <summary>A kernel trace that records what the session does with it.</summary>
    private sealed class FakeTrace : IKernelFileIoTrace
    {
        private readonly ManualResetEventSlim _end = new();
        private Action<FileIoRecord>? _onFileIo;
        private Exception? _processingFailure;
        private int _flushes;

        public Exception? OpenThrows { get; init; }
        public string? OpenedName { get; private set; }
        public int OpenedBufferMb { get; private set; }
        public ManualResetEventSlim Processing { get; } = new();
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool ProcessReturned { get; private set; }
        public int Flushes => Volatile.Read(ref _flushes);
        public int? Lost { get; set; }
        public int? EventsLost => Lost;

        public void Open(string sessionName, int bufferSizeMb)
        {
            if (OpenThrows is not null)
            {
                throw OpenThrows;
            }

            OpenedName = sessionName;
            OpenedBufferMb = bufferSizeMb;
        }

        public void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
        {
            _onFileIo = onFileIo;
            Processing.Set();
            _end.Wait();
            ProcessReturned = true;
            if (_processingFailure is not null)
            {
                throw _processingFailure;
            }
        }

        public void Emit(FileIoRecord record) => _onFileIo!(record);

        /// <summary>The session is stopped from outside: Process returns normally, nobody called Stop.</summary>
        public void EndFromOutside() => _end.Set();

        public void FailProcessing(Exception failure)
        {
            _processingFailure = failure;
            _end.Set();
        }

        public void Flush() => Interlocked.Increment(ref _flushes);

        public void Stop()
        {
            Stopped = true;
            _end.Set();
        }

        public void Dispose() => Disposed = true;
    }
}
