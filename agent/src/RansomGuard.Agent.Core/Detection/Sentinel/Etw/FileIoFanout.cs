namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>
/// Lets more than one consumer read the kernel FileIO session (AGT-ATTR-001).
///
/// <see cref="EtwFileIoSession.Start"/> takes a single callback, and SENTINEL's canary filter was
/// its only subscriber. ENTROPY needs the same events to name the process that wrote a file, and a
/// second session would take another of the machine's eight system-logger slots for records the
/// first one already carries. This dispatches one record to every subscriber instead.
///
/// It runs on the session's own thread, for every FileIO event of the machine -- tens of thousands
/// a second during a copy -- so it does exactly what a hot loop may do: walk an array and call.
/// Subscribers are held in an immutable array swapped on subscribe, so the hot path never locks
/// and never allocates.
///
/// <para>
/// A subscriber that throws is caught and counted (<see cref="Faults"/>), never rethrown: SENTINEL's
/// filter must keep receiving even if another consumer is broken. The exception is not logged here
/// -- logging on the session thread is what AGT-AUD-002 forbids -- the count is read by the module
/// that owns the session.
/// </para>
/// </summary>
public sealed class FileIoFanout
{
    private readonly object _gate = new();
    private Action<FileIoRecord>[] _subscribers = [];
    private long _faults;
    private long _dispatched;

    /// <summary>Records dispatched since the start.</summary>
    public long Dispatched => Interlocked.Read(ref _dispatched);

    /// <summary>Exceptions swallowed from subscribers since the start; 0 is the healthy value.</summary>
    public long Faults => Interlocked.Read(ref _faults);

    /// <summary>Subscribers currently registered.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Length;
            }
        }
    }

    /// <summary>
    /// Registers a consumer. Safe to call while the session runs: the hot path reads the array
    /// reference once, so it sees either the old set or the new one, never a half-built array.
    /// </summary>
    public void Subscribe(Action<FileIoRecord> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _subscribers = [.. _subscribers, handler];
        }
    }

    /// <summary>The session callback. Never blocks, never throws.</summary>
    public void OnFileIo(FileIoRecord record)
    {
        // One volatile-ish read of the reference; the array itself is never mutated in place.
        Action<FileIoRecord>[] subscribers = _subscribers;
        Interlocked.Increment(ref _dispatched);
        foreach (Action<FileIoRecord> subscriber in subscribers)
        {
            try
            {
                subscriber(record);
            }
            catch
            {
                // A broken consumer must not stop the others, and must not stop the session.
                Interlocked.Increment(ref _faults);
            }
        }
    }
}
