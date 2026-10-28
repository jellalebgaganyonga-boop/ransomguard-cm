using System.Collections.Concurrent;
using System.Diagnostics;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>The outcome of an attribution (ADR-025, section 3).</summary>
public enum AttributionState
{
    /// <summary>One writing process, identified by (PID, creation time).</summary>
    Resolved,

    /// <summary>No process can be named; <see cref="AttributionOutcome.Reason"/> says why.</summary>
    Unavailable,

    /// <summary>Several processes wrote the canary in the window: none is named.</summary>
    Ambiguous,
}

/// <summary>Closed reason codes of an attribution that names nobody (ADR-025, section 3).</summary>
public static class AttributionReason
{
    /// <summary>No write event reached the session for this operation.</summary>
    public const string NoWriteEvent = "no_write_event";

    /// <summary>The only writes were carried by PID 4: the system's deferred or mapped-page writes.</summary>
    public const string SystemPid = "system_pid";

    /// <summary>The PID now belongs to a process created after the write: it was reused.</summary>
    public const string PidReused = "pid_reused";

    /// <summary>The writer has exited, and its creation time was not seen: it cannot be verified.</summary>
    public const string ProcessExited = "process_exited";

    /// <summary>The ETW session is not running: the watcher alone saw the operation.</summary>
    public const string EtwUnavailable = "etw_unavailable";
}

/// <summary>An attribution: who wrote the canary, or why nobody is named.</summary>
public sealed record AttributionOutcome(
    AttributionState State,
    int? ProcessId = null,
    DateTime? ProcessCreatedAtUtc = null,
    string? ProcessName = null,
    string? ImagePath = null,
    string? Reason = null)
{
    /// <summary>The wire value of <see cref="State"/>.</summary>
    public string StateCode => State switch
    {
        AttributionState.Resolved => "resolved",
        AttributionState.Ambiguous => "ambiguous",
        _ => "unavailable",
    };
}

/// <summary>A running process as the attribution needs it: creation time, name, image.</summary>
/// <param name="CreatedAtUtc">
/// The wall-clock creation time. What a person reads on the alert, and what the response engine
/// re-checks against the live process before it suspends anything.
/// </param>
/// <param name="Name">The process name, when it could be read.</param>
/// <param name="ImagePath">The image on disk, when it could be read.</param>
/// <param name="SessionCreatedAtUtc">
/// The same creation time as the ETW session recorded it, when the session saw the process
/// start; null when it did not.
///
/// <para>
/// It exists because the two clocks are not the same one. An ETW session timestamps every
/// record against the clock it was opened with, and does not follow the wall clock afterwards.
/// Put the machine's clock forward while the agent runs -- which is what happens on a bench VM
/// with no Guest Additions, corrected by hand -- and the session's write records stay in the old
/// frame while <see cref="System.Diagnostics.Process.StartTime"/> jumps to the new one. Compared
/// against each other, every process looks as though it were created after the write it made.
/// </para>
///
/// <para>
/// So the ordering question "was this process created after that write?" is answered with both
/// times from the session's clock, and this is the one in that frame.
/// </para>
/// </param>
public sealed record ProcessFacts(
    DateTime CreatedAtUtc,
    string? Name,
    string? ImagePath,
    DateTime? SessionCreatedAtUtc = null);

/// <summary>
/// The creation times of processes, so that an attribution is a (PID, creation time) pair and a
/// reused PID is never mistaken for the writer (ADR-025, 4b). Two sources: the session's
/// Process/Start events for processes born while it runs (0.6 to 2.6 ms from GetProcessTimes,
/// measured), and an immediate read of the running process otherwise.
/// </summary>
public sealed class ProcessCreationTimes
{
    /// <summary>Start events kept, at most; the oldest half is dropped beyond it.</summary>
    public const int Capacity = 20_000;

    private readonly ConcurrentDictionary<int, DateTime> _started = new();
    private readonly Func<int, ProcessFacts?> _readProcess;

    /// <summary>Creates the store.</summary>
    /// <param name="readProcess">Reads a running process; the OS by default. Null when it has exited.</param>
    public ProcessCreationTimes(Func<int, ProcessFacts?>? readProcess = null)
    {
        _readProcess = readProcess ?? ReadRunningProcess;
    }

    /// <summary>The session callback for Process/Start. Never blocks.</summary>
    public void OnProcessStart(ProcessStartRecord record)
    {
        _started[record.ProcessId] = record.StartUtc;
        if (_started.Count > Capacity)
        {
            foreach (int pid in _started.OrderBy(p => p.Value).Take(Capacity / 2).Select(p => p.Key))
            {
                _started.TryRemove(pid, out _);
            }
        }
    }

    /// <summary>
    /// The facts of a PID now: the wall-clock creation time for people and for the response
    /// engine, and the session's own recording of it for the ordering comparison.
    /// </summary>
    /// <remarks>
    /// The two times used to be reconciled into one, keeping the session's if the two agreed
    /// within 50 ms and the OS's otherwise. That is exactly backwards when a clock is corrected:
    /// the disagreement becomes hours, the OS time wins, and it is then compared against write
    /// records that are still in the session's old frame -- so the writer looks younger than its
    /// own write and the alert comes back pid_reused with nothing suspended. They are kept apart
    /// now, and each question is answered with the clock that can answer it.
    /// </remarks>
    public ProcessFacts? Read(int pid)
    {
        ProcessFacts? running = _readProcess(pid);
        if (_started.TryGetValue(pid, out DateTime started))
        {
            // The session saw this process start. Its own timestamp is the one that can be
            // compared with the writes it recorded, whatever the wall clock has done since.
            return running is null
                ? new ProcessFacts(started, null, null, started)
                : running with { SessionCreatedAtUtc = started };
        }

        // No start event: this process was already running when the session opened, so it
        // predates every write the session could have recorded. There is no session-frame time
        // for it, and none is needed.
        return running;
    }

    private static ProcessFacts? ReadRunningProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string? image = null;
            try
            {
                image = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // a protected or exiting process: the image stays unknown, the PID pair stands
            }

            return new ProcessFacts(process.StartTime.ToUniversalTime(), process.ProcessName, image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null; // exited, or unreadable
        }
    }
}

/// <summary>
/// Names the process that wrote a canary, from the ETW writes a detection gathered (ADR-025,
/// commit 4 of 8). A wrong PID is worse than no PID:
/// - only writes count (the filter lets nothing else through), never PID 0 or 4, never the agent;
/// - two writing PIDs in the window: ambiguous;
/// - the writer is identified by (PID, creation time); a process created after the write holds a
///   reused PID: unavailable / pid_reused.
/// The decision is taken <see cref="Delay"/> after the first signal, whatever arrived by then.
/// </summary>
public sealed class CanaryAttributor
{
    /// <summary>When the attribution is decided, after the detection's first signal.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1500);

    private readonly ProcessCreationTimes _processes;

    /// <summary>Creates the attributor.</summary>
    public CanaryAttributor(ProcessCreationTimes processes)
    {
        _processes = processes;
    }

    /// <summary>Decides the attribution of a detection from the writes it gathered.</summary>
    public AttributionOutcome Resolve(CanaryDetection detection, bool etwRunning)
    {
        IReadOnlyList<FileIoRecord> writes = detection.Writes;
        if (!etwRunning && writes.Count == 0)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.EtwUnavailable);
        }

        List<FileIoRecord> attributable = writes.Where(w => w.ProcessId is not (0 or 4)).ToList();
        if (attributable.Count == 0)
        {
            return new AttributionOutcome(
                AttributionState.Unavailable,
                Reason: writes.Count > 0 ? AttributionReason.SystemPid : AttributionReason.NoWriteEvent);
        }

        int[] writers = attributable.Select(w => w.ProcessId).Distinct().ToArray();
        if (writers.Length > 1)
        {
            return new AttributionOutcome(AttributionState.Ambiguous);
        }

        int pid = writers[0];
        DateTime firstWrite = attributable.Min(w => w.TimestampUtc);
        ProcessFacts? facts = _processes.Read(pid);
        if (facts is null)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.ProcessExited);
        }

        if (BornAfter(facts, firstWrite))
        {
            // The process holding this PID now was born after the write: not the writer.
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.PidReused);
        }

        return new AttributionOutcome(AttributionState.Resolved, pid, facts.CreatedAtUtc, facts.Name, facts.ImagePath);
    }

    /// <summary>
    /// Was the process holding this PID created after the write -- that is, is this a reused PID?
    /// </summary>
    /// <remarks>
    /// Both times must come from the same clock. An ETW session timestamps its records against
    /// the clock it was opened with and does not follow the wall clock afterwards, so on a
    /// machine whose time is corrected while the agent runs the two drift apart by however far
    /// the clock moved. Comparing across them made every writer look younger than its own write:
    /// on PC-RECEPTION on 2026-10-27, after the clock went forward about nine hours, every alert
    /// came back pid_reused and nothing was suspended.
    ///
    /// <para>
    /// When the session saw the process start, that is the time to use: it and the write record
    /// are both the session's, so the answer is exact whatever the clock has done.
    /// </para>
    ///
    /// <para>
    /// When it did not, the process was already running before the session opened — and a
    /// creation time Windows recorded then is in the same frame as the session's own records,
    /// because a later clock change rewrites neither. So the wall-clock times may be compared
    /// with each other; it is only comparing one against the other that was wrong.
    /// </para>
    ///
    /// <para>
    /// Widening a tolerance was not an option: PID reuse is what this comparison exists to
    /// catch, and a window wide enough to absorb a clock correction is wide enough to let a
    /// recycled PID through.
    /// </para>
    /// </remarks>
    internal static bool BornAfter(ProcessFacts facts, DateTime firstWriteUtc) =>
        facts.SessionCreatedAtUtc is { } inSessionFrame
            ? inSessionFrame > firstWriteUtc
            : facts.CreatedAtUtc > firstWriteUtc;
}
