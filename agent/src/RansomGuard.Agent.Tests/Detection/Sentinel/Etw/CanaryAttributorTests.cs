using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// Who wrote the canary (ADR-025, commit 4). A wrong PID is worse than no PID: every case that
/// cannot name the writer with certainty names nobody, and says why.
/// </summary>
public sealed class CanaryAttributorTests
{
    private const string Canary = @"C:\Patients\0001_dossier_patient.docx";
    private static readonly DateTime WriteAt = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<int, ProcessFacts> _running = new()
    {
        [4242] = new ProcessFacts(WriteAt.AddMinutes(-3), "evil", @"C:\Users\x\AppData\Local\Temp\evil.exe"),
        [5151] = new ProcessFacts(WriteAt.AddMinutes(-9), "other", @"C:\Tools\other.exe"),
    };

    private readonly ProcessCreationTimes _processes;
    private readonly CanaryAttributor _attributor;

    /// <summary>
    /// The wall clock, held still beside the session's. Every start event is read as a pair --
    /// the session's stamp and this -- and the difference between them is the offset the
    /// attribution uses to read a write time in the frame a process creation time is in. Held at
    /// the moment of the write, so an undisturbed machine has an offset of zero.
    /// </summary>
    private readonly ManualTimeProvider _wallClock = new();

    public CanaryAttributorTests()
    {
        _wallClock.SetWallClock(WriteAt);
        _processes = new ProcessCreationTimes(pid => _running.GetValueOrDefault(pid), _wallClock);
        _attributor = new CanaryAttributor(_processes);
    }

    [Fact]
    public void One_writer_running_is_resolved_by_PID_creation_time_name_and_image()
    {
        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Resolved);
        outcome.StateCode.ShouldBe("resolved");
        outcome.ProcessId.ShouldBe(4242);
        outcome.ProcessCreatedAtUtc.ShouldBe(WriteAt.AddMinutes(-3));
        outcome.ProcessName.ShouldBe("evil");
        outcome.ImagePath.ShouldBe(@"C:\Users\x\AppData\Local\Temp\evil.exe");
        outcome.Reason.ShouldBeNull();
    }

    [Fact]
    public void Several_writes_of_one_writer_are_still_one_writer()
    {
        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242), Write(4242, 30), Write(4, 60)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Resolved);
        outcome.ProcessId.ShouldBe(4242);
    }

    [Fact]
    public void Two_writers_in_the_window_name_nobody()
    {
        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242), Write(5151, 200)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Ambiguous);
        outcome.StateCode.ShouldBe("ambiguous");
        outcome.ProcessId.ShouldBeNull();
    }

    [Fact]
    public void Only_system_writes_name_nobody_reason_system_pid()
    {
        // A mapped page or a deferred write: a detection, without attribution (ADR-025, 4d).
        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4), Write(4, 10)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe("system_pid");
        outcome.ProcessId.ShouldBeNull();
    }

    [Fact]
    public void PID_0_never_attributes()
    {
        _attributor.Resolve(Detection(Write(0)), etwRunning: true).Reason.ShouldBe("system_pid");
    }

    [Fact]
    public void No_write_while_ETW_runs_names_nobody_reason_no_write_event()
    {
        AttributionOutcome outcome = _attributor.Resolve(Detection(), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe("no_write_event");
    }

    [Fact]
    public void No_write_and_no_ETW_names_nobody_reason_etw_unavailable()
    {
        _attributor.Resolve(Detection(), etwRunning: false).Reason.ShouldBe("etw_unavailable");
    }

    [Fact]
    public void A_PID_now_held_by_a_process_born_after_the_write_is_reused_and_names_nobody()
    {
        _running[4242] = new ProcessFacts(WriteAt.AddSeconds(1), "innocent-medical-app", @"C:\Program Files\Imaging\viewer.exe");

        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe("pid_reused");
        outcome.ProcessId.ShouldBeNull();
        outcome.ProcessName.ShouldBeNull();
    }

    [Fact]
    public void A_writer_that_exited_unseen_names_nobody_reason_process_exited()
    {
        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(7777)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe("process_exited");
        outcome.ProcessId.ShouldBeNull();
    }

    [Fact]
    public void A_writer_that_exited_after_its_start_was_seen_is_resolved_by_its_start_time()
    {
        _processes.OnProcessStart(new ProcessStartRecord(7777, WriteAt.AddSeconds(-20)));

        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(7777)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Resolved);
        outcome.ProcessId.ShouldBe(7777);
        outcome.ProcessCreatedAtUtc.ShouldBe(WriteAt.AddSeconds(-20));
        outcome.ProcessName.ShouldBeNull(); // gone: the pair stands, the name is unknown
    }

    [Fact]
    public void A_start_event_and_a_running_process_that_disagree_mean_the_PID_was_reused()
    {
        // The start seen in the session belongs to an earlier process; the running one is newer.
        _processes.OnProcessStart(new ProcessStartRecord(4242, WriteAt.AddHours(-1)));
        _running[4242] = new ProcessFacts(WriteAt.AddSeconds(5), "newcomer", null);

        _attributor.Resolve(Detection(Write(4242)), etwRunning: true).Reason.ShouldBe("pid_reused");
    }

    [Fact]
    public void The_session_stamp_and_the_OS_time_are_both_kept_for_their_own_question()
    {
        _processes.OnProcessStart(new ProcessStartRecord(4242, WriteAt.AddMinutes(-3).AddMilliseconds(3)));

        ProcessFacts facts = _processes.Read(4242).ShouldNotBeNull();

        // The wall-clock time is what a person reads and what the response engine re-checks
        // against the live process, so it is kept as Windows reports it; the session's own
        // stamp is kept beside it for the ordering question, not folded into it.
        facts.CreatedAtUtc.ShouldBe(WriteAt.AddMinutes(-3));
        facts.SessionCreatedAtUtc.ShouldBe(WriteAt.AddMinutes(-3).AddMilliseconds(3));
        facts.Name.ShouldBe("evil");                                           // the running process's name
    }

    [Fact]
    public void The_start_events_kept_are_bounded()
    {
        for (int pid = 1; pid <= ProcessCreationTimes.Capacity + 10; pid++)
        {
            _processes.OnProcessStart(new ProcessStartRecord(100_000 + pid, WriteAt.AddSeconds(pid)));
        }

        _processes.Read(100_001).ShouldBeNull(); // among the oldest, dropped
        _processes.Read(100_000 + ProcessCreationTimes.Capacity + 10).ShouldNotBeNull();
    }

    /// <summary>
    /// The owner corrects a bench VM's clock while the agent is running, and attribution keeps
    /// working.
    ///
    /// <para>
    /// The ETW session timestamps every record against the clock it was opened with. Move the
    /// wall clock and the session does not follow: its write records stay in the old frame while
    /// <c>Process.StartTime</c> jumps to the new one. The two were compared against each other,
    /// so every process looked as if it had been created after the write it made, and every
    /// alert came back unavailable / pid_reused with nothing suspended. Seen on PC-RECEPTION on
    /// 2026-10-27 after the clock was put forward about nine hours, and again on PC-LAB.
    /// </para>
    ///
    /// <para>
    /// These VMs have no Guest Additions, so their clocks free-run and are corrected by hand:
    /// this is not an edge case on this bench, it is Tuesday.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(3)]    // the clock put forward, which is the case that was seen
    [InlineData(-3)]   // and backward, which the same comparison breaks the other way
    [InlineData(9.5)]  // the jump actually made on PC-RECEPTION
    public void A_wall_clock_change_while_the_agent_runs_still_names_the_writer(double hours)
    {
        // The clock was corrected, so Windows now reads that many hours away from the session.
        _wallClock.SetWallClock(WriteAt.AddHours(hours));
        // The session saw this process start, 20 seconds before the write, on the session clock.
        _processes.OnProcessStart(new ProcessStartRecord(4242, WriteAt.AddSeconds(-20)));
        // The wall clock then moved, so the OS reports the same process with a creation time
        // that many hours away from anything the session recorded.
        _running[4242] = new ProcessFacts(
            WriteAt.AddSeconds(-20).AddHours(hours), "powershell",
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Resolved);
        outcome.ProcessId.ShouldBe(4242);
        outcome.ProcessName.ShouldBe("powershell");
        // The time reported is the one a person reads and the one the response engine re-checks
        // against the live process: the wall clock, not the session's drifted frame.
        outcome.ProcessCreatedAtUtc.ShouldBe(WriteAt.AddSeconds(-20).AddHours(hours));
    }

    [Fact]
    public void A_reused_PID_is_still_refused_after_a_clock_change()
    {
        // The guard that must survive the fix. The session saw this PID start AFTER the write,
        // which is the real signature of reuse -- both times from the session's own clock, so
        // the comparison holds whatever the wall clock has done since.
        _wallClock.SetWallClock(WriteAt.AddHours(3));
        _processes.OnProcessStart(new ProcessStartRecord(4242, WriteAt.AddSeconds(1)));
        _running[4242] = new ProcessFacts(WriteAt.AddSeconds(1).AddHours(3), "innocent-viewer",
            @"C:\Program Files\Imaging\viewer.exe");

        AttributionOutcome outcome = _attributor.Resolve(Detection(Write(4242)), etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe("pid_reused");
        outcome.ProcessId.ShouldBeNull();
    }

    private static CanaryDetection Detection(params FileIoRecord[] writes)
    {
        var correlator = new CanaryCorrelator(new ManualTimeProvider());
        CanaryDetection detection = correlator.OnWatcher(Canary, CanaryOperation.Modification)!;
        foreach (FileIoRecord write in writes)
        {
            correlator.OnEtw(write).ShouldBeNull(); // joins the detection
        }

        return detection;
    }

    private static FileIoRecord Write(int pid, int afterMs = 0) =>
        new(FileIoOperation.Write, Canary, pid, WriteAt.AddMilliseconds(afterMs), 0, 0);
}
