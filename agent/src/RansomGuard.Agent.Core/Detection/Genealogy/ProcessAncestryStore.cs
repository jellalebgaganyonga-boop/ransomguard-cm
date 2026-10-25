using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Keeps the ancestry of processes that wrote a watched file, captured while they were still
/// running.
///
/// <para>
/// GENEALOGY used to start from the Restart Manager, which answers "who holds this file open right
/// now". Ransomware writes and closes, so by the time an alert is enriched the Restart Manager
/// names nobody and the tree was never built -- that is what
/// <c>attribution_unreliable</c> meant for this module, and why its measured attribution was 0/120
/// (AttributionBench).
/// </para>
///
/// <para>
/// The kernel FileIO session sees the write as it happens, so the writer is still alive then. This
/// store takes that moment: <see cref="OnWriterSeen"/> is called from the session's fan-out and
/// does nothing but drop a PID into a bounded channel -- it must never block that thread
/// (AGT-AUD-002), and WMI, which the snapshot needs for the command line, costs tens of
/// milliseconds. A background loop drains the channel and captures the process and its ancestors
/// within milliseconds, which is early enough that short-lived writers are usually still there.
/// </para>
///
/// <para>
/// Entries are keyed by (PID, start time), never PID alone: Windows reuses PIDs within minutes and
/// a tree built on a reused PID would name an innocent process. A lookup that cannot match the
/// start time answers nothing rather than the wrong thing.
/// </para>
/// </summary>
public sealed class ProcessAncestryStore : IDisposable
{
    /// <summary>PIDs waiting to be captured; a full queue drops the arriving one and counts it.</summary>
    public const int QueueCapacity = 256;

    /// <summary>Trees kept at most; the oldest quarter is dropped past it.</summary>
    public const int Capacity = 4_096;

    /// <summary>How long a captured tree stays answerable.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

    private readonly Channel<int> _pending = Channel.CreateBounded<int>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<(int Pid, long StartTicks), Captured> _trees = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _recentlyRequested = new();
    private readonly IProcessSnapshotService _snapshots;
    private readonly ILogger<ProcessAncestryStore> _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stopping = new();
    private readonly int _agentPid;
    private Task? _drain;
    private long _requested;
    private long _dropped;
    private long _captured;
    private long _missed;

    /// <summary>Creates the store.</summary>
    public ProcessAncestryStore(
        IProcessSnapshotService snapshots,
        ILogger<ProcessAncestryStore> logger,
        TimeProvider? time = null,
        int? agentPid = null)
    {
        _snapshots = snapshots;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _agentPid = agentPid ?? Environment.ProcessId;
    }

    /// <summary>PIDs the session asked to capture.</summary>
    public long Requested => Interlocked.Read(ref _requested);

    /// <summary>Requests dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Trees captured.</summary>
    public long Captured_ => Interlocked.Read(ref _captured);

    /// <summary>PIDs that had already exited when the loop reached them.</summary>
    public long Missed => Interlocked.Read(ref _missed);

    /// <summary>Trees held right now.</summary>
    public int TrackedTrees => _trees.Count;

    /// <summary>
    /// Whether the store is doing its job: the drain loop is running and, once anything has been
    /// requested, it is capturing rather than missing everything. A store that never captures is
    /// what GENEALOGY reports as degraded.
    /// </summary>
    public bool IsHealthy =>
        _drain is { IsCompleted: false }
        && (Requested == 0 || Captured_ > 0);

    /// <summary>Starts the background capture loop. Idempotent.</summary>
    public void Start()
    {
        _drain ??= Task.Run(DrainAsync);
    }

    /// <summary>
    /// The fan-out callback: a process wrote a watched file, so capture its ancestry before it
    /// exits. Runs on the session thread -- one dictionary probe and one non-blocking write.
    /// </summary>
    public void OnWriterSeen(int pid)
    {
        // PID 0 and 4 are the system's deferred writes and never attribute; the agent is not a
        // suspect either.
        if (pid is 0 or 4 || pid == _agentPid)
        {
            return;
        }

        // One capture per PID per retention window: a copy generates thousands of writes from one
        // process and the queue must not fill with the same PID.
        DateTimeOffset now = _time.GetUtcNow();
        if (_recentlyRequested.TryGetValue(pid, out DateTimeOffset asked) && now - asked < Retention)
        {
            return;
        }

        _recentlyRequested[pid] = now;
        Interlocked.Increment(ref _requested);
        if (!_pending.Writer.TryWrite(pid))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// The ancestry of <paramref name="pid"/>, if it was captured and the start time matches.
    ///
    /// <paramref name="startedAtUtc"/> is the writer's creation time as the attribution resolved
    /// it. A captured tree whose root started at a different moment belongs to a different process
    /// that happened to get the same PID: it is refused, not returned.
    /// </summary>
    public ProcessTree? TryGet(int pid, DateTime? startedAtUtc)
    {
        foreach (KeyValuePair<(int Pid, long StartTicks), Captured> entry in _trees)
        {
            if (entry.Key.Pid != pid)
            {
                continue;
            }

            if (startedAtUtc is not null
                && Math.Abs(entry.Key.StartTicks - startedAtUtc.Value.Ticks) > TimeSpan.TicksPerSecond)
            {
                continue; // same PID, another process: reused
            }

            return entry.Value.Tree;
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _pending.Writer.TryComplete();
        try
        {
            _drain?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // the loop is cancelled; nothing to report at shutdown
        }

        _stopping.Dispose();
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (int pid in _pending.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                Capture(pid);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            // The loop ending means GENEALOGY stops capturing; IsHealthy reports it and the module
            // state follows. It must not take the host down.
            _logger.LogError(ex, "GENEALOGY ancestry capture loop stopped");
        }
    }

    private void Capture(int pid)
    {
        ProcessTree? tree = _snapshots.BuildProcessTree(pid);
        if (tree is null || tree.Root.StartTime is null)
        {
            // Already gone, or unreadable: nothing is invented for it.
            Interlocked.Increment(ref _missed);
            return;
        }

        _trees[(pid, tree.Root.StartTime.Value.Ticks)] = new Captured(tree, _time.GetUtcNow());
        Interlocked.Increment(ref _captured);

        if (_trees.Count > Capacity)
        {
            Trim();
        }
    }

    private void Trim()
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - Retention;
        foreach (KeyValuePair<(int Pid, long StartTicks), Captured> entry in _trees)
        {
            if (entry.Value.CapturedAt < cutoff)
            {
                _trees.TryRemove(entry.Key, out _);
            }
        }

        foreach ((int Pid, long StartTicks) key in _trees
                     .OrderBy(e => e.Value.CapturedAt)
                     .Take(Math.Max(0, _trees.Count - Capacity))
                     .Select(e => e.Key)
                     .ToArray())
        {
            _trees.TryRemove(key, out _);
        }

        foreach (KeyValuePair<int, DateTimeOffset> entry in _recentlyRequested)
        {
            if (entry.Value < cutoff)
            {
                _recentlyRequested.TryRemove(entry.Key, out _);
            }
        }
    }

    private readonly record struct Captured(ProcessTree Tree, DateTimeOffset CapturedAt);
}
