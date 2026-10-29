using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// What rule 3 needs and nothing had been keeping (AGT-EN-002): how many *different* files in one
/// folder turned high-entropy recently, and by how much on average.
///
/// <para>
/// One file rewritten ten times is a noisy application. Ten files rewritten once each, in the same
/// folder, within a minute, is mass encryption -- the strongest ransomware signal the agent has.
/// Only the first entry for a given file counts, so a single loud file can never reach the
/// threshold on its own.
/// </para>
///
/// <para>
/// After the rule fires for a folder the window is put on <see cref="Cooldown"/>: a burst of fifty
/// files must raise one Critical alert naming the folder, not fifty. The cooldown is lifted by
/// time alone, so a second wave after it is reported again.
/// </para>
///
/// <para>
/// Bounded: at most <see cref="MaxDirectories"/> folders and <see cref="MaxFilesPerDirectory"/>
/// files each, and entries older than the window are swept on every record.
/// </para>
/// </summary>
public sealed class DirectoryEntropyWindow
{
    /// <summary>Folders tracked at most; the least recently touched are dropped past it.</summary>
    public const int MaxDirectories = 2_048;

    /// <summary>Distinct files remembered per folder; enough for any sane threshold.</summary>
    public const int MaxFilesPerDirectory = 512;

    private readonly ConcurrentDictionary<string, DirectoryState> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeSpan _window;
    private readonly TimeProvider _time;

    /// <summary>Creates the window.</summary>
    /// <param name="windowSeconds">Entropy.DirectoryShiftWindowSeconds.</param>
    /// <param name="time">Clock; the system clock by default.</param>
    public DirectoryEntropyWindow(int windowSeconds, TimeProvider? time = null)
    {
        _window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How long a folder stays silent after rule 3 fired for it: the window itself.</summary>
    public TimeSpan Cooldown => _window;

    /// <summary>Folders currently tracked.</summary>
    public int TrackedDirectories => _directories.Count;

    /// <summary>
    /// Records a per-file entropy alert and returns what rule 3 should be evaluated against.
    ///
    /// <paramref name="writerPid"/> is the attributed writer when there is one; the snapshot says
    /// whether every file in the window agrees on it, which is what lets the folder-wide alert name
    /// the process without guessing.
    /// </summary>
    public DirectorySnapshot Record(string filePath, double delta, int? writerPid)
    {
        string directory = DirectoryOf(filePath);
        DateTimeOffset now = _time.GetUtcNow();

        DirectoryState state = _directories.GetOrAdd(directory, static _ => new DirectoryState());
        DirectorySnapshot snapshot = state.Record(filePath, delta, writerPid, now, _window);

        if (_directories.Count > MaxDirectories)
        {
            Trim(now);
        }

        return snapshot;
    }

    /// <summary>
    /// Marks the folder of <paramref name="filePath"/> as having fired, so the rest of the burst
    /// stays quiet for <see cref="Cooldown"/>.
    /// </summary>
    public void MarkFired(string filePath)
    {
        if (_directories.TryGetValue(DirectoryOf(filePath), out DirectoryState? state))
        {
            state.MarkFired(_time.GetUtcNow());
        }
    }

    /// <summary>The folder the rule names, for a file the window has seen.</summary>
    public static string DirectoryOf(string filePath)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? filePath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetDirectoryName(filePath) ?? filePath;
        }
    }

    private void Trim(DateTimeOffset now)
    {
        foreach (string directory in _directories
                     .OrderBy(e => e.Value.LastTouched)
                     .Take(MaxDirectories / 4)
                     .Select(e => e.Key)
                     .ToArray())
        {
            _directories.TryRemove(directory, out _);
        }

        // Folders whose entries have all aged out carry nothing worth keeping either.
        foreach (KeyValuePair<string, DirectoryState> entry in _directories)
        {
            if (entry.Value.IsEmpty(now - _window))
            {
                _directories.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed class DirectoryState
    {
        private readonly Dictionary<string, (double Delta, DateTimeOffset At, int? WriterPid)> _files =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new();
        private DateTimeOffset _firedAt = DateTimeOffset.MinValue;

        public DateTimeOffset LastTouched { get; private set; } = DateTimeOffset.MinValue;

        public DirectorySnapshot Record(
            string filePath, double delta, int? writerPid, DateTimeOffset now, TimeSpan window)
        {
            lock (_gate)
            {
                LastTouched = now;
                DateTimeOffset cutoff = now - window;

                foreach (string stale in _files.Where(f => f.Value.At < cutoff).Select(f => f.Key).ToArray())
                {
                    _files.Remove(stale);
                }

                // The first entry for a file is the one that counts: a file rewritten ten times is
                // one file, not ten. Its delta is refreshed so the average follows the worst of it.
                if (_files.TryGetValue(filePath, out (double Delta, DateTimeOffset At, int? WriterPid) existing))
                {
                    _files[filePath] = (Math.Max(existing.Delta, delta), existing.At, existing.WriterPid ?? writerPid);
                }
                else if (_files.Count < MaxFilesPerDirectory)
                {
                    _files[filePath] = (delta, now, writerPid);
                }

                bool inCooldown = _firedAt > DateTimeOffset.MinValue && now - _firedAt < window;
                int count = _files.Count;
                double average = count == 0 ? 0 : _files.Values.Average(f => f.Delta);

                // One writer for every file that has one, and at least one: the folder-wide alert
                // can name it. Files with no attribution do not contradict, they just do not vote.
                int[] writers = [.. _files.Values.Where(f => f.WriterPid is not null).Select(f => f.WriterPid!.Value).Distinct()];
                int? unanimous = writers.Length == 1 ? writers[0] : null;

                return new DirectorySnapshot(count, average, inCooldown, unanimous);
            }
        }

        public void MarkFired(DateTimeOffset now)
        {
            lock (_gate)
            {
                _firedAt = now;
            }
        }

        public bool IsEmpty(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                return _files.Count == 0 || _files.Values.All(f => f.At < cutoff);
            }
        }
    }
}

/// <summary>What rule 3 is evaluated against for one folder.</summary>
/// <param name="FileCount">Distinct files that turned high-entropy inside the window.</param>
/// <param name="AverageDelta">Mean entropy rise across them.</param>
/// <param name="InCooldown">The folder already raised a folder-wide alert inside the window.</param>
/// <param name="UnanimousWriterPid">The one process that wrote every attributed file, when there is one.</param>
public readonly record struct DirectorySnapshot(
    int FileCount,
    double AverageDelta,
    bool InCooldown,
    int? UnanimousWriterPid);
