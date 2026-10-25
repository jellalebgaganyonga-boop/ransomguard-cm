using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// The paths the agent is writing itself, so its own work is never reported as an attack.
///
/// <para>
/// ETW carries the writing PID and the agent's own is filtered out there. The FileSystemWatcher
/// carries no PID at all -- it says only that a file changed -- so a path the agent is about to
/// write is marked here first, and the watcher event that follows is dropped.
/// </para>
///
/// <para>
/// Shared between the deployment service, which creates the canaries, and the monitor, which
/// watches them. It used to live inside the monitor, which meant the deployment's own writes were
/// invisible to it: a fresh install planted its decoys, the watcher saw each creation, and the
/// console showed a burst of Critical "canary modified" alerts before anything had happened. A
/// clean install must report nothing.
/// </para>
/// </summary>
public sealed class AgentOwnWrites
{
    /// <summary>
    /// Grace for a restoration, as the FileSystemWatcher sees it: the agent rewrites one file and
    /// the watcher reports it at once. Deliberately short -- a canary the agent has just repaired
    /// goes straight back under watch, so a write to it a few seconds later is still an attack and
    /// is still reported.
    /// </summary>
    public static readonly TimeSpan RestoreGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Grace for deployment, as the watcher sees it. Longer than a restoration's: the canaries are
    /// created before the monitor's watchers exist, so their events surface once watching starts,
    /// which is after the monitor's own startup delay.
    /// </summary>
    public static readonly TimeSpan DeploymentGrace = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the kernel may take to report a write the agent has made.
    ///
    /// <para>
    /// The watcher is prompt; the kernel is not. Windows' cache manager flushes a file long after
    /// the process wrote it, and ETW attributes that flush to a system PID rather than to the
    /// writer, so filtering the agent's own PID out of the session does not cover the agent's own
    /// writes. Measured on the bench: canaries written at 17:51:28, the same writes surfacing at
    /// 17:51:46 -- eighteen seconds later, as <c>system_pid</c>.
    /// </para>
    ///
    /// <para>
    /// This window is used only by the kernel path, and it is deliberately not the same number as
    /// the watcher's. Making the watcher wait this long would mean a decoy the agent had just
    /// repaired was unwatched for a minute, and a write to it in that time went unreported --
    /// which is a detection gap, not a fix. The watcher keeps its short grace and keeps raising
    /// those alerts; the kernel path only avoids raising a second one for the agent's own write.
    /// </para>
    /// </summary>
    public static readonly TimeSpan KernelReportDelay = TimeSpan.FromSeconds(60);

    /// <summary>When the agent wrote a path, and how long the watcher's grace on it lasts.</summary>
    private readonly record struct OwnWrite(DateTime WrittenAt, DateTime WatcherGraceUntil);

    private readonly ConcurrentDictionary<string, OwnWrite> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates the set.</summary>
    public AgentOwnWrites(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Paths currently marked.</summary>
    public int Count => _paths.Count;

    /// <summary>Marks a path the agent is about to write, for <paramref name="grace"/>.</summary>
    public void Mark(string path, TimeSpan grace)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;
        _paths[path] = new OwnWrite(now, now.Add(grace));
    }

    /// <summary>
    /// Whether a watcher event on <paramref name="path"/> is the agent's own write. An expired
    /// mark is removed as it is read, so the set does not grow.
    /// </summary>
    public bool IsOwn(string path)
    {
        if (!_paths.TryGetValue(path, out OwnWrite own))
        {
            return false;
        }

        if (_time.GetUtcNow().UtcDateTime < own.WatcherGraceUntil)
        {
            return true;
        }

        // The watcher is done with this mark, but the kernel may still be about to report the
        // same write, so the entry stays until the kernel window has passed too.
        if (_time.GetUtcNow().UtcDateTime - own.WrittenAt >= KernelReportDelay)
        {
            _paths.TryRemove(path, out _);
        }

        return false;
    }

    /// <summary>
    /// Whether a write the kernel has just reported on <paramref name="path"/> is one the agent
    /// made. Asked by the ETW path, which sees a write long after it happened and attributed to a
    /// system PID rather than to the writer -- see <see cref="KernelReportDelay"/>.
    ///
    /// <para>
    /// This does not widen what the watcher forgives: a write the watcher reports inside its own
    /// short grace is the agent's, and anything after that is an attack and is alerted on. All
    /// this does is stop the kernel raising a second alert, minutes later, for a write the agent
    /// made itself.
    /// </para>
    /// </summary>
    public bool WasWrittenByAgent(string path)
    {
        if (!_paths.TryGetValue(path, out OwnWrite own))
        {
            return false;
        }

        if (_time.GetUtcNow().UtcDateTime - own.WrittenAt < KernelReportDelay)
        {
            return true;
        }

        _paths.TryRemove(path, out _);
        return false;
    }

    /// <summary>Drops a mark before its grace ends (the agent's write failed, so nothing follows).</summary>
    public void Clear(string path) => _paths.TryRemove(path, out _);
}
