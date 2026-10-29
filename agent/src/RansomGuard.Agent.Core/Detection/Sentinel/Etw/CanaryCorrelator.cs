namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>What happened to a canary, the class of the correlation key (ADR-025, section 3).</summary>
public enum CanaryOperation
{
    /// <summary>Its content changed (watcher Changed; ETW Write, end of file, a writing Create).</summary>
    Modification,

    /// <summary>It was renamed (watcher Renamed, by its old path; ETW Rename, SetInfo rename).</summary>
    Rename,

    /// <summary>It was deleted (watcher Deleted; ETW Delete, SetInfo delete disposition).</summary>
    Deletion,
}

/// <summary>Which source saw the operation first.</summary>
public enum SignalSource
{
    /// <summary>The FileSystemWatcher.</summary>
    Watcher,

    /// <summary>The ETW FileIO session.</summary>
    Etw,
}

/// <summary>
/// One detection: one operation on one canary, whichever source saw it first and however many
/// signals followed in the window. The ETW writes it gathers feed the attribution (commit 4).
/// </summary>
public sealed class CanaryDetection
{
    private readonly List<FileIoRecord> _writes = [];

    internal CanaryDetection(string path, CanaryOperation operation, SignalSource firstSource, DateTimeOffset firstSeen)
    {
        Path = path;
        Operation = operation;
        FirstSource = firstSource;
        FirstSeen = firstSeen;
    }

    /// <summary>Identifies the detection, and later its alert and the attribution complement.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The canary's path (for a rename, its old path).</summary>
    public string Path { get; }

    /// <summary>The class of operation.</summary>
    public CanaryOperation Operation { get; }

    /// <summary>The source that saw it first: the alert leaves on that signal.</summary>
    public SignalSource FirstSource { get; }

    /// <summary>When the first signal arrived.</summary>
    public DateTimeOffset FirstSeen { get; }

    /// <summary>The watcher saw it (in the window).</summary>
    public bool SeenByWatcher { get; internal set; }

    /// <summary>ETW saw it (in the window).</summary>
    public bool SeenByEtw { get; internal set; }

    /// <summary>The ETW writes of the window, in arrival order.</summary>
    public IReadOnlyList<FileIoRecord> Writes
    {
        get
        {
            lock (_writes)
            {
                return [.. _writes];
            }
        }
    }

    internal void Add(FileIoRecord record)
    {
        lock (_writes)
        {
            _writes.Add(record);
        }
    }
}

/// <summary>
/// One detection per operation (ADR-025, commit 3 of 8). The watcher and the ETW session both see a
/// write on a canary -- the watcher in 10 to 16 ms, ETW in 97/203 ms (p50/p95, flushed every
/// 100 ms) -- and some writes only one of them sees (a rewrite through a mapped view: ETW only).
/// The alert leaves on the first signal; the second, on the same key within
/// <see cref="Window"/>, joins it and raises nothing.
///
/// Key: (canary path, case-insensitive; operation class). Window: 2 s from the first signal; after
/// it, a new operation is a new detection. For SENTINEL's canaries this key replaces the 500 ms
/// FileEventDeduplicator.
///
/// A window closed with the watcher only, while the ETW session was running, is an ETW miss:
/// counted (<see cref="EtwMisses"/>) for the heartbeat. The reverse -- ETW only -- is expected
/// (mapped writes).
/// </summary>
public sealed class CanaryCorrelator
{
    /// <summary>The correlation window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(string Path, CanaryOperation Operation), CanaryDetection> _open =
        new(new KeyComparer());

    private long _etwMisses;

    /// <summary>Creates the correlator.</summary>
    public CanaryCorrelator(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Whether the ETW session is running: a watcher-only window then counts as an ETW miss.</summary>
    public bool EtwRunning { get; set; }

    /// <summary>Windows closed with the watcher only while ETW was running.</summary>
    public long EtwMisses => Interlocked.Read(ref _etwMisses);

    /// <summary>The operation class of an ETW write (the filter only lets writes through); null otherwise.</summary>
    public static CanaryOperation? Classify(FileIoRecord record) => record.Operation switch
    {
        FileIoOperation.Write or FileIoOperation.Create => CanaryOperation.Modification,
        FileIoOperation.Rename => CanaryOperation.Rename,
        FileIoOperation.Delete => CanaryOperation.Deletion,
        FileIoOperation.SetInfo => record.InfoClass switch
        {
            EtwCanaryFilter.FileRenameInformation or EtwCanaryFilter.FileRenameInformationEx => CanaryOperation.Rename,
            EtwCanaryFilter.FileDispositionInformation or EtwCanaryFilter.FileDispositionInformationEx => CanaryOperation.Deletion,
            EtwCanaryFilter.FileEndOfFileInformation => CanaryOperation.Modification,
            _ => null,
        },
        _ => null,
    };

    /// <summary>A watcher signal. Returns the detection to raise, or null when it joins an open one.</summary>
    public CanaryDetection? OnWatcher(string path, CanaryOperation operation)
    {
        lock (_gate)
        {
            Sweep();
            if (_open.TryGetValue((path, operation), out CanaryDetection? open))
            {
                open.SeenByWatcher = true;
                return null;
            }

            var detection = new CanaryDetection(path, operation, SignalSource.Watcher, _time.GetUtcNow()) { SeenByWatcher = true };
            _open[(path, operation)] = detection;
            return detection;
        }
    }

    /// <summary>An ETW write. Returns the detection to raise, or null when it joins an open one.</summary>
    public CanaryDetection? OnEtw(FileIoRecord record)
    {
        if (Classify(record) is not CanaryOperation operation)
        {
            return null;
        }

        lock (_gate)
        {
            Sweep();
            if (_open.TryGetValue((record.FileName, operation), out CanaryDetection? open))
            {
                open.SeenByEtw = true;
                open.Add(record);
                return null;
            }

            var detection = new CanaryDetection(record.FileName, operation, SignalSource.Etw, _time.GetUtcNow()) { SeenByEtw = true };
            detection.Add(record);
            _open[(record.FileName, operation)] = detection;
            return detection;
        }
    }

    /// <summary>Closes the windows that are over; called on every signal, and by the monitor's polling.</summary>
    public void Sweep()
    {
        lock (_gate)
        {
            DateTimeOffset now = _time.GetUtcNow();
            List<(string, CanaryOperation)>? closed = null;
            foreach (KeyValuePair<(string Path, CanaryOperation Operation), CanaryDetection> entry in _open)
            {
                if (now - entry.Value.FirstSeen < Window)
                {
                    continue;
                }

                if (EtwRunning && entry.Value.SeenByWatcher && !entry.Value.SeenByEtw)
                {
                    Interlocked.Increment(ref _etwMisses);
                }

                (closed ??= []).Add(entry.Key);
            }

            foreach ((string, CanaryOperation) key in closed ?? [])
            {
                _open.Remove(key);
            }
        }
    }

    private sealed class KeyComparer : IEqualityComparer<(string Path, CanaryOperation Operation)>
    {
        public bool Equals((string Path, CanaryOperation Operation) x, (string Path, CanaryOperation Operation) y) =>
            x.Operation == y.Operation && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, CanaryOperation Operation) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Path), key.Operation);
    }
}
