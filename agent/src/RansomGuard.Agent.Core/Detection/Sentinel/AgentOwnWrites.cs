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
    /// <summary>Grace for a restoration: the agent rewrites one file and the event follows at once.</summary>
    public static readonly TimeSpan RestoreGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Grace for deployment. Longer than a restoration's: the canaries are created before the
    /// monitor's watchers exist, and the events for them can surface once watching starts, which
    /// is after the monitor's own startup delay.
    /// </summary>
    public static readonly TimeSpan DeploymentGrace = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTime> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates the set.</summary>
    public AgentOwnWrites(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Paths currently marked.</summary>
    public int Count => _paths.Count;

    /// <summary>Marks a path the agent is about to write, for <paramref name="grace"/>.</summary>
    public void Mark(string path, TimeSpan grace) =>
        _paths[path] = _time.GetUtcNow().UtcDateTime.Add(grace);

    /// <summary>
    /// Whether a watcher event on <paramref name="path"/> is the agent's own write. An expired
    /// mark is removed as it is read, so the set does not grow.
    /// </summary>
    public bool IsOwn(string path)
    {
        if (!_paths.TryGetValue(path, out DateTime graceUntil))
        {
            return false;
        }

        if (_time.GetUtcNow().UtcDateTime < graceUntil)
        {
            return true;
        }

        _paths.TryRemove(path, out _);
        return false;
    }

    /// <summary>Drops a mark before its grace ends (the agent's write failed, so nothing follows).</summary>
    public void Clear(string path) => _paths.TryRemove(path, out _);
}
