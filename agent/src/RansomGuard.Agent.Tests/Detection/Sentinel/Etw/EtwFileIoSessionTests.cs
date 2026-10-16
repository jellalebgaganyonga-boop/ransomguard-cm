using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The SENTINEL ETW FileIO session (ADR-025, commit 1): its lifecycle, its flush cadence and its
/// failure reasons, on a fake kernel trace and a manual clock. The real session is exercised by
/// <see cref="EtwFileIoSessionLiveTests"/>.
/// </summary>
public sealed class EtwFileIoSessionTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeTrace _trace = new();
    private readonly RecordingLogger<EtwFileIoSession> _log = new();
    private readonly List<FileIoRecord> _records = [];
    private readonly EtwFileIoSession _session;

    public EtwFileIoSessionTests()
    {
        _session = new EtwFileIoSession(() => _trace, _log, _time, () => ["EventLog-System", "SYSMON TRACE"]);
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public void It_opens_its_own_named_session_with_64_MB_of_buffers()
    {
        StartAndWait().ShouldBeTrue();

        _trace.OpenedName.ShouldBe("RansomGuard-Sentinel-FileIO");
        _trace.OpenedName!.ShouldNotContain("ExfilWatch"); // separate from EXFIL WATCH's session
        _trace.OpenedBufferMb.ShouldBe(64);
        _session.State.ShouldBe(EtwSessionState.Running);
        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.None);
    }

    [Fact]
    public void It_flushes_every_100_ms_and_not_in_between()
    {
        StartAndWait();

        _time.Advance(TimeSpan.FromMilliseconds(99));
        _trace.Flushes.ShouldBe(0);
        for (int i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        _trace.Flushes.ShouldBe(10);
        _session.Flushes.ShouldBe(10);
    }

    [Fact]
    public void The_records_reach_the_subscriber()
    {
        StartAndWait();
        var record = new FileIoRecord(FileIoOperation.Write, @"C:\canaries\a.txt", 4321, DateTime.UtcNow, 0, 0);

        _trace.Emit(record);

        _records.ShouldBe([record]);
    }

    [Fact]
    public void Without_administrator_rights_it_is_unavailable_says_why_and_flushes_nothing()
    {
        _trace.OpenThrows = new UnauthorizedAccessException("Access Denied (Administrator rights required to start ETW)");

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.State.ShouldBe(EtwSessionState.Unavailable);
        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.AdminRightsMissing);
        _time.ActiveTimers.ShouldBe(0);
        _trace.Disposed.ShouldBeTrue();
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("etw_admin_rights_missing"));
    }

    [Fact]
    public void With_no_session_slot_left_it_says_so_distinctly_and_lists_the_active_sessions()
    {
        _trace.OpenThrows = new COMException("Insufficient system resources exist", unchecked((int)0x800705AA));

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.NoSessionSlot);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("etw_no_session_slot") && e.Message.Contains("SYSMON TRACE"));
        _log.Entries.ShouldNotContain(e => e.Message.Contains("etw_admin_rights_missing"));
    }

    [Fact]
    public void Any_other_failure_to_open_is_a_start_failure_logged_as_an_error()
    {
        _trace.OpenThrows = new COMException("The instance name passed was not recognized", unchecked((int)0x80071069));

        _session.Start(_records.Add, _ => { }).ShouldBeFalse();

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.StartFailed);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("etw_start_failed"));
    }

    [Fact]
    public void A_failure_while_processing_makes_it_unavailable_and_stops_the_flushes()
    {
        StartAndWait();

        _trace.FailProcessing(new InvalidOperationException("real-time buffers lost"));
        Eventually(() => _session.State == EtwSessionState.Unavailable);

        _session.UnavailableReason.ShouldBe(EtwUnavailableReason.ProcessingFailed);
        _time.ActiveTimers.ShouldBe(0);
        _log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("etw_processing_failed"));
    }

    [Fact]
    public void Stop_stops_the_trace_waits_for_its_thread_and_flushes_no_more()
    {
        StartAndWait();

        _session.Stop();
        _time.Advance(TimeSpan.FromSeconds(1));

        _session.State.ShouldBe(EtwSessionState.Stopped);
        _trace.Stopped.ShouldBeTrue();
        _trace.Disposed.ShouldBeTrue();
        _trace.ProcessReturned.ShouldBeTrue();
        _trace.Flushes.ShouldBe(0);
        _time.ActiveTimers.ShouldBe(0);
        Should.NotThrow(_session.Stop); // idempotent
    }

    [Fact]
    public void Events_lost_is_null_when_unknown_never_zero()
    {
        _session.EventsLost.ShouldBeNull(); // not started

        StartAndWait();
        _trace.Lost = null;
        _session.EventsLost.ShouldBeNull();
        _trace.Lost = 12;
        _session.EventsLost.ShouldBe(12);
    }

    private bool StartAndWait()
    {
        bool started = _session.Start(_records.Add, _ => { });
        if (started)
        {
            _trace.Processing.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the processing thread never called Process");
        }

        return started;
    }

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
        private readonly ManualResetEventSlim _stop = new();
        private Action<FileIoRecord>? _onFileIo;
        private Exception? _processingFailure;
        private int _flushes;

        public Exception? OpenThrows { get; set; }
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
            _stop.Wait();
            ProcessReturned = true;
            if (_processingFailure is not null)
            {
                throw _processingFailure;
            }
        }

        public void Emit(FileIoRecord record) => _onFileIo!(record);

        public void FailProcessing(Exception failure)
        {
            _processingFailure = failure;
            _stop.Set();
        }

        public void Flush() => Interlocked.Increment(ref _flushes);

        public void Stop()
        {
            Stopped = true;
            _stop.Set();
        }

        public void Dispose() => Disposed = true;
    }
}
