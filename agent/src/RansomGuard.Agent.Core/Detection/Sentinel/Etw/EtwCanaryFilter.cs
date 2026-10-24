using System.Collections.Frozen;
using System.Threading.Channels;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>
/// The only subscriber of <see cref="EtwFileIoSession"/> (ADR-025, commit 2 of 8). It runs on the
/// session's thread, for every FileIO event of the machine -- tens of thousands a second during a
/// copy -- and must never block it (AGT-AUD-002). Per event: a directory-prefix test, then an exact
/// lookup in an immutable set of canary paths (7 to 10 ns measured), then the operation test. What
/// passes goes into a channel of <see cref="Capacity"/> records; when it is full, the arriving
/// record is refused and counted -- the first records are kept, because the first writer is the one
/// to attribute.
///
/// What passes: a write on a canary (ADR-025, 2.4) -- Write; SetInfo of a rename, a delete
/// disposition or an end of file; Delete; Rename; a Create whose disposition writes. An opening
/// (FILE_OPEN, FILE_OPEN_IF) never passes: antivirus, indexer and backup open the canaries for
/// reading all the time (81 770 openings a minute by the probe's reader). A write carried by PID 4
/// (the system's deferred writes) passes: it is a detection, without attribution (ADR-025, 2a).
/// The agent's own operations never pass (deployment, regeneration, integrity reads).
/// </summary>
public sealed class EtwCanaryFilter
{
    /// <summary>Records kept waiting for the consumer.</summary>
    public const int Capacity = 1024;

    // FILE_INFORMATION_CLASS values of a SetInfo that writes the canary away.
    internal const int FileRenameInformation = 10;
    internal const int FileDispositionInformation = 13;
    internal const int FileEndOfFileInformation = 20;
    internal const int FileDispositionInformationEx = 64;
    internal const int FileRenameInformationEx = 65;

    // NT dispositions of a Create that writes: FILE_SUPERSEDE, FILE_CREATE, FILE_OVERWRITE, FILE_OVERWRITE_IF.
    private static readonly FrozenSet<uint> WritingDispositions = new uint[] { 0, 2, 4, 5 }.ToFrozenSet();

    private static readonly FrozenSet<int> WritingInfoClasses = new[]
    {
        FileRenameInformation, FileDispositionInformation, FileEndOfFileInformation,
        FileDispositionInformationEx, FileRenameInformationEx,
    }.ToFrozenSet();

    private readonly Channel<FileIoRecord> _channel = Channel.CreateBounded<FileIoRecord>(
        new BoundedChannelOptions(Capacity)
        {
            // Wait + TryWrite: a full channel refuses the arriving record without blocking, and
            // TryWrite says so, which makes the loss countable.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

    private readonly int _agentPid;
    private volatile CanarySet _canaries = CanarySet.Empty;
    private long _lost;

    /// <summary>Creates the filter.</summary>
    /// <param name="agentPid">The agent's own PID, whose operations never pass; the current process by default.</param>
    public EtwCanaryFilter(int? agentPid = null)
    {
        _agentPid = agentPid ?? Environment.ProcessId;
    }

    /// <summary>The canary writes that passed, for the correlation (commit 3).</summary>
    public ChannelReader<FileIoRecord> Reader => _channel.Reader;

    /// <summary>Canary writes refused because the channel was full, since the start.</summary>
    public long Lost => Interlocked.Read(ref _lost);

    /// <summary>
    /// Replaces the canaries in one block (deployment, regeneration). The session's thread sees
    /// either the old set or the new one, never a mix, and never waits.
    /// </summary>
    public void SetCanaries(IEnumerable<string> canaryPaths) => _canaries = CanarySet.Of(canaryPaths);

    /// <summary>
    /// The canary's path as SENTINEL stores it, for a path ETW or the watcher reports with another
    /// case; false when the path is not a canary.
    /// </summary>
    public bool TryGetCanary(string fileName, out string canaryPath) => _canaries.TryGet(fileName, out canaryPath);

    /// <summary>The session callback. Never blocks.</summary>
    public void OnFileIo(FileIoRecord record)
    {
        if (record.ProcessId == _agentPid || !_canaries.TryGet(record.FileName, out string canaryPath) || !IsWrite(record))
        {
            return;
        }

        // The record goes on with the stored path: the correlation key and the repository lookup
        // need the canary's own spelling, not the kernel's.
        if (!_channel.Writer.TryWrite(record with { FileName = canaryPath }))
        {
            Interlocked.Increment(ref _lost);
        }
    }

    /// <summary>Whether the operation writes the file (ADR-025, 2.4); an opening for reading does not.</summary>
    public static bool IsWrite(FileIoRecord record) => record.Operation switch
    {
        FileIoOperation.Write or FileIoOperation.Delete or FileIoOperation.Rename => true,
        FileIoOperation.SetInfo => WritingInfoClasses.Contains(record.InfoClass),
        FileIoOperation.Create => WritingDispositions.Contains(record.CreateDisposition),
        _ => false,
    };

    /// <summary>An immutable set of canary paths, with their directories for the cheap first test.</summary>
    private sealed class CanarySet
    {
        public static readonly CanarySet Empty = new(FrozenDictionary<string, string>.Empty, []);

        // Full path (any case) -> the path as given, i.e. as SENTINEL stores it.
        private readonly FrozenDictionary<string, string> _paths;
        private readonly string[] _directories;

        private CanarySet(FrozenDictionary<string, string> paths, string[] directories)
        {
            _paths = paths;
            _directories = directories;
        }

        public static CanarySet Of(IEnumerable<string> canaryPaths)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string canaryPath in canaryPaths)
            {
                paths[Path.GetFullPath(canaryPath)] = canaryPath;
            }

            string[] directories = paths.Keys
                .Select(p => Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new CanarySet(paths.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), directories);
        }

        public bool TryGet(string fileName, out string canaryPath)
        {
            foreach (string directory in _directories)
            {
                if (fileName.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    return _paths.TryGetValue(fileName, out canaryPath!);
                }
            }

            canaryPath = string.Empty;
            return false;
        }
    }
}
