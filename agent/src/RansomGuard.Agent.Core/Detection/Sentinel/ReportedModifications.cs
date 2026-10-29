using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// The decoys whose modification the real-time path has reported and is still putting right, so
/// the periodic integrity sweep does not report the same write a second time.
///
/// <para>
/// Two things watch a decoy. The FileSystemWatcher and the ETW session raise an alert when the
/// write happens, and can name the writer because a write event is there to match. The periodic
/// sweep raises one when a decoy's contents stop matching its stored hash; it is the safety net
/// for a write the real-time path missed, and it has no writer to name.
/// </para>
///
/// <para>
/// Between the real-time alert and the end of the restore that follows it, the decoy on disk does
/// not match its stored hash -- that is precisely what the alert was about. A sweep landing in
/// that interval found the mismatch, knew nothing of the alert a moment earlier, and raised a
/// second Critical alert for the same write, with attribution <c>no_write_event</c> and no process
/// chain. On the bench on 2026-10-26 one write produced two alerts a second apart, and a reader
/// who opened the later one saw neither the writer nor what started it.
/// </para>
///
/// <para>
/// This is deliberately not a time window that forgives everything for a while. A path is entered
/// when the alert is raised and removed as soon as the restore has stored the new hash, so the
/// sweep is silent for exactly the modification already reported and no longer. A write that
/// lands after that restore finds nothing recorded here and is reported as its own attack.
/// </para>
/// </summary>
public sealed class ReportedModifications
{
    /// <summary>
    /// How long an entry may stay before it is ignored anyway.
    ///
    /// <para>
    /// Only a safety net. The entry is normally removed when the restore ends, including when the
    /// restore fails; this covers the case where the handling path dies between the two without
    /// removing it, and makes sure a decoy cannot be silenced for the rest of the agent's life by
    /// one lost entry. A restore that takes longer than this is already a fault being reported
    /// elsewhere.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MaxHandlingTime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTime> _handling = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates the set.</summary>
    public ReportedModifications(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Paths currently being handled.</summary>
    public int Count => _handling.Count;

    /// <summary>A modification on this path has been reported; its restore is about to run.</summary>
    public void Reported(string path) => _handling[path] = _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Whether the modification the sweep is looking at is the one already reported and being put
    /// right. An entry past <see cref="MaxHandlingTime"/> is dropped as it is read, so the set
    /// does not grow and a lost entry stops silencing its decoy.
    /// </summary>
    public bool IsBeingHandled(string path)
    {
        if (!_handling.TryGetValue(path, out DateTime reportedAt))
        {
            return false;
        }

        if (_time.GetUtcNow().UtcDateTime - reportedAt < MaxHandlingTime)
        {
            return true;
        }

        _handling.TryRemove(path, out _);
        return false;
    }

    /// <summary>
    /// The reported modification has been dealt with: the decoy is restored and its new hash
    /// stored, or the restore failed and the decoy has left surveillance. Either way the sweep
    /// speaks for this path again.
    /// </summary>
    public void Handled(string path) => _handling.TryRemove(path, out _);
}
