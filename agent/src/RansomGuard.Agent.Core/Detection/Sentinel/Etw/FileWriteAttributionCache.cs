using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>
/// Remembers who wrote a file recently, so a detector that reads the file afterwards can still name
/// the writer (AGT-ATTR-001).
///
/// <para>
/// SENTINEL knows its writer because a canary write is correlated as it happens. ENTROPY cannot:
/// it is told a file changed, then reads it and computes its entropy, which takes long enough for
/// the writer to have closed its handle. The Restart Manager (GENEALOGY's source) only reports
/// processes *currently holding* the file, so for a write-and-close -- what ransomware does -- it
/// reports nobody. This cache keeps the ETW write records for a short window instead, keyed by
/// path, and answers after the fact.
/// </para>
///
/// <para>
/// Bounded on purpose. Only writes under <see cref="SetWatchedDirectories"/> are kept (ENTROPY's
/// own folders, not the whole machine), only for <see cref="Retention"/>, and at most
/// <see cref="Capacity"/> paths; past that the oldest paths are dropped. A cache that grew with
/// the machine's file activity would be a memory leak on a busy workstation.
/// </para>
///
/// <para>
/// The attribution rules are the same as <see cref="CanaryAttributor"/>'s, deliberately: a wrong
/// PID is worse than no PID. PID 0 and 4 (the system's deferred and mapped-page writes) never
/// attribute, the agent's own writes never attribute, two writers in the window are
/// <see cref="AttributionState.Ambiguous"/>, and a PID whose process was created after the write
/// has been reused -- <see cref="AttributionReason.PidReused"/>.
/// </para>
/// </summary>
public sealed class FileWriteAttributionCache
{
    /// <summary>How long a write stays answerable. ENTROPY reads and hashes within seconds.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);

    /// <summary>Paths tracked at most; the oldest quarter is dropped past it.</summary>
    public const int Capacity = 20_000;

    /// <summary>Writes kept per path; beyond it the path is already ambiguous.</summary>
    public const int WritesPerPath = 8;

    private readonly ConcurrentDictionary<string, PathWrites> _writes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ProcessCreationTimes _processes;
    private readonly TimeProvider _time;
    private readonly int _agentPid;
    private volatile string[] _directories = [];
    private long _recorded;
    private long _dropped;

    /// <summary>Creates the cache.</summary>
    /// <param name="processes">Process creation times, shared with SENTINEL's attribution.</param>
    /// <param name="time">Clock for retention; the system clock by default.</param>
    /// <param name="agentPid">The agent's own PID, whose writes never attribute; the current process by default.</param>
    public FileWriteAttributionCache(ProcessCreationTimes processes, TimeProvider? time = null, int? agentPid = null)
    {
        _processes = processes;
        _time = time ?? TimeProvider.System;
        _agentPid = agentPid ?? Environment.ProcessId;
    }

    /// <summary>Write records kept right now.</summary>
    public int TrackedPaths => _writes.Count;

    /// <summary>Writes recorded since the start.</summary>
    public long Recorded => Interlocked.Read(ref _recorded);

    /// <summary>Paths dropped for capacity since the start.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// The folders whose writes are kept. Anything outside them is ignored, which is what keeps
    /// the cache small. Replaced in one block; the session thread sees one set or the other.
    /// </summary>
    public void SetWatchedDirectories(IEnumerable<string> directories)
    {
        _directories = directories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d =>
            {
                string full = Path.GetFullPath(d);
                return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The fan-out callback. Runs on the session thread: a prefix test, then a dictionary update.
    /// Never blocks, never throws.
    /// </summary>
    public void OnFileIo(FileIoRecord record)
    {
        // Same exclusions as the canary attributor, applied before anything is stored: a record
        // that could never attribute is not worth the memory.
        if (record.ProcessId is 0 or 4 || record.ProcessId == _agentPid || !EtwCanaryFilter.IsWrite(record))
        {
            return;
        }

        // Prefix test on the raw name first: it rejects everything outside ENTROPY's folders, which
        // on a busy machine is nearly every record, before paying for normalisation.
        if (!IsWatched(record.FileName))
        {
            return;
        }

        // Both sides of the cache normalise the same way, or a lookup would silently miss a write
        // that is there (the watcher and the kernel can spell the same file differently).
        PathWrites entry = _writes.GetOrAdd(SafeFullPath(record.FileName), static _ => new PathWrites());
        entry.Add(record, _time.GetUtcNow().UtcDateTime);
        Interlocked.Increment(ref _recorded);

        if (_writes.Count > Capacity)
        {
            Trim();
        }
    }

    /// <summary>
    /// Who wrote <paramref name="path"/> in the retention window, or why nobody is named.
    ///
    /// <paramref name="etwRunning"/> false answers <see cref="AttributionReason.EtwUnavailable"/>
    /// rather than "no write event": a detector that cannot see writes has not observed an absence.
    /// </summary>
    public AttributionOutcome Resolve(string path, bool etwRunning)
    {
        if (!etwRunning)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.EtwUnavailable);
        }

        DateTime now = _time.GetUtcNow().UtcDateTime;
        string key = SafeFullPath(path);
        if (!_writes.TryGetValue(key, out PathWrites? entry))
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.NoWriteEvent);
        }

        (int Pid, DateTime WrittenAt)[] fresh = entry.Fresh(now - Retention);
        if (fresh.Length == 0)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.NoWriteEvent);
        }

        int[] writers = fresh.Select(w => w.Pid).Distinct().ToArray();
        if (writers.Length > 1)
        {
            return new AttributionOutcome(AttributionState.Ambiguous);
        }

        int pid = writers[0];
        DateTime firstWrite = fresh.Min(w => w.WrittenAt);
        ProcessFacts? facts = _processes.Read(pid);
        if (facts is null)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.ProcessExited);
        }

        // Both times from the session's own clock; see CanaryAttributor.BornAfter. A wall-clock
        // correction while the agent runs moves Process.StartTime and leaves the session's
        // records where they were, and comparing across the two named nobody for every write.
        if (CanaryAttributor.BornAfter(facts, firstWrite, _processes.SessionToWallOffset))
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.PidReused);
        }

        return new AttributionOutcome(AttributionState.Resolved, pid, facts.CreatedAtUtc, facts.Name, facts.ImagePath);
    }

    private bool IsWatched(string fileName)
    {
        string[] directories = _directories;
        foreach (string directory in directories)
        {
            if (fileName.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Drops the quarter of paths whose newest write is oldest. Called from the session thread when
    // the cache is over capacity; a busy machine pays it rarely because only watched folders count.
    private void Trim()
    {
        foreach (string path in _writes
                     .OrderBy(e => e.Value.NewestUtc)
                     .Take(Capacity / 4)
                     .Select(e => e.Key)
                     .ToArray())
        {
            if (_writes.TryRemove(path, out _))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
    }

    // ETW reports a full path already; GetFullPath is for the caller's spelling (relative, or with
    // a different separator). A path the OS refuses is used as given rather than throwing.
    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>The writes of one path, newest kept, oldest dropped past <see cref="WritesPerPath"/>.</summary>
    private sealed class PathWrites
    {
        private readonly Queue<(int Pid, DateTime WrittenAt)> _items = new();

        public DateTime NewestUtc { get; private set; } = DateTime.MinValue;

        public void Add(FileIoRecord record, DateTime seenAtUtc)
        {
            // The kernel timestamp is what the attribution compares against the process creation
            // time; the clock reading is only the retention stamp.
            DateTime writtenAt = record.TimestampUtc == default ? seenAtUtc : record.TimestampUtc;
            lock (_items)
            {
                _items.Enqueue((record.ProcessId, writtenAt));
                while (_items.Count > WritesPerPath)
                {
                    _items.Dequeue();
                }

                if (seenAtUtc > NewestUtc)
                {
                    NewestUtc = seenAtUtc;
                }
            }
        }

        public (int Pid, DateTime WrittenAt)[] Fresh(DateTime cutoffUtc)
        {
            lock (_items)
            {
                return [.. _items.Where(i => i.WrittenAt >= cutoffUtc)];
            }
        }
    }
}
