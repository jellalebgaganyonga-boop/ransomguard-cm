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
    /// Grace for a restoration. It was 3 seconds, on the assumption that the event follows the
    /// rewrite at once. It does not: the FileSystemWatcher is prompt, but the kernel's view is
    /// not. Windows' cache manager flushes a file long after the process wrote it, and ETW
    /// attributes that flush to a system PID rather than to the writer, so the agent's own
    /// rewrite comes back as somebody else's write with no way left to recognise it. Measured on
    /// the bench: canaries written at 17:51:28, the same writes surfacing at 17:51:46 -- eighteen
    /// seconds. Three seconds meant every restoration raised a fresh alert, which triggered
    /// another restoration, until SENTINEL reported a sustained attack on a file only it had
    /// touched.
    ///
    /// <para>
    /// The cost is stated rather than hidden: for this long after the agent repairs a decoy, a
    /// write to that one path is taken for the agent's own. An attacker who writes to a canary in
    /// that window, on that exact path, is not reported for it -- though the canaries it has not
    /// just repaired are all still watched, and the same write would still reach ENTROPY.
    /// </para>
    /// </summary>
    public static readonly TimeSpan RestoreGrace = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Grace for deployment: the canaries are created before the monitor's watchers exist, the
    /// events for them surface once watching starts, and the kernel's own view of those writes
    /// arrives later still (see <see cref="RestoreGrace"/>).
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
