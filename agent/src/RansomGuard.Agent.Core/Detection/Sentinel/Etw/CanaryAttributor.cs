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
public sealed record ProcessFacts(DateTime CreatedAtUtc, string? Name, string? ImagePath);

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

    /// <summary>The facts of a PID now: creation time from the start event when seen, else from the OS.</summary>
    public ProcessFacts? Read(int pid)
    {
        ProcessFacts? running = _readProcess(pid);
        if (_started.TryGetValue(pid, out DateTime started))
        {
            // The start event is the creation time of the process the event saw; the running
            // process, if any, gives the name and the image (and its own creation time, compared
            // by the caller).
            return running is null
                ? new ProcessFacts(started, null, null)
                : running with { CreatedAtUtc = Math.Abs((running.CreatedAtUtc - started).TotalMilliseconds) <= 50 ? started : running.CreatedAtUtc };
        }

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

        if (facts.CreatedAtUtc > firstWrite)
        {
            // The process holding this PID now was born after the write: not the writer.
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.PidReused);
        }

        return new AttributionOutcome(AttributionState.Resolved, pid, facts.CreatedAtUtc, facts.Name, facts.ImagePath);
    }
}
