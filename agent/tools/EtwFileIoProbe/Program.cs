// ETW FileIO probe -- measurement instrument for the ETW design gate (SPRINT_8_ARBITRAGE_LOT3.md,
// B.2). NOT product code: it answers, with numbers, what a kernel FileIO session costs and what
// it carries, before any line of ETW attribution is written. See README.md.
//
// Usage:
//   EtwFileIoProbe measure [--seconds 60] [--agent-pid N] [--report report.md]
//       baseline (no session), then three keyword sets; each phase: rest, then a file copy
//       workload run by a child process (its PID is the one attribution must return).
//   EtwFileIoProbe no-admin
//       what a non-administrator agent gets: the kernel session refused, the FileSystemWatcher
//       still firing on a canary write.
//   EtwFileIoProbe writer <workDir> <seconds>      (internal: the copy workload, child process)
//
// It only touches files it creates under %TEMP%\rg-etw-probe, deleted at the end.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string mode = args.Length > 0 ? args[0] : "measure";
return mode switch
{
    "writer" => Writer.Run(args[1], int.Parse(args[2], CultureInfo.InvariantCulture)),
    "reader" => ConcurrentReader.Run(args[1]),
    "no-admin" => NoAdmin.Run(),
    "measure" => Measure.Run(args),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: EtwFileIoProbe measure [--seconds 60] [--agent-pid N] [--report file.md] | no-admin");
    return 2;
}

/// <summary>The work directory: canaries, a copy source, a copy destination.</summary>
internal static class Work
{
    // RG_PROBE_WORK: a directory the agent under measurement watches, so that its "today" CPU
    // covers the same copy. The root itself is kept (a watcher on it would be orphaned).
    public static readonly string Root =
        Environment.GetEnvironmentVariable("RG_PROBE_WORK") is { Length: > 0 } work
            ? work
            : Path.Combine(Path.GetTempPath(), "rg-etw-probe");
    public static string Canaries => Path.Combine(Root, "canaries");
    public static string Source => Path.Combine(Root, "source");
    public static string Destination => Path.Combine(Root, "destination");
    public const int CanaryCount = 20;
    public const int SourceFiles = 500;
    public const int SourceFileBytes = 64 * 1024;

    public static void Create()
    {
        Clean();
        Directory.CreateDirectory(Canaries);
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
        var random = new Random(42);
        var buffer = new byte[SourceFileBytes];
        for (int i = 0; i < SourceFiles; i++)
        {
            random.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(Source, $"doc_{i:D4}.bin"), buffer);
        }

        for (int i = 0; i < CanaryCount; i++)
        {
            File.WriteAllText(Path.Combine(Canaries, $"canary_{i:D2}.txt"), $"canary {i}\n" + new string('x', 4096));
        }

        for (int i = 0; i < MappedCount; i++)
        {
            random.NextBytes(buffer);
            File.WriteAllBytes(MappedPath(i), buffer);
        }
    }

    public static string CanaryPath(int i) => Path.Combine(Canaries, $"canary_{i:D2}.txt");

    public const int MappedCount = 5;
    public const string MappedPrefix = "mapped_";

    public static string MappedPath(int i) => Path.Combine(Canaries, $"{MappedPrefix}{i:D2}.txt");

    /// <summary>Deletes what the probe created; the root stays.</summary>
    public static void Clean()
    {
        foreach (string dir in new[] { Canaries, Source, Destination })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>
/// The copy workload, in its own process: copies the source tree repeatedly, and on each pass does
/// to one canary what ransomware does -- rewrite it encrypted, rename it, delete it -- then puts it
/// back. Prints its operation counts at the end.
/// </summary>
internal static class Writer
{
    public static int Run(string root, int seconds)
    {
        string source = Path.Combine(root, "source");
        string destination = Path.Combine(root, "destination");
        string canaries = Path.Combine(root, "canaries");
        var deadline = Stopwatch.StartNew();
        long copies = 0, rewrites = 0, renames = 0, deletes = 0, mappedWrites = 0;
        int pass = 0;
        string[] files = Directory.GetFiles(source);
        while (deadline.Elapsed.TotalSeconds < seconds)
        {
            foreach (string file in files)
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
                copies++;
                if (deadline.Elapsed.TotalSeconds >= seconds) break;
            }

            // Pattern 7: rewrite a canary through a memory-mapped view, as some ransomware does. The
            // pages reach the disk as paging I/O: which PID does ETW give those writes?
            string mapped = Path.Combine(canaries, $"{Work.MappedPrefix}{pass % Work.MappedCount:D2}.txt");
            using (var file = MemoryMappedFile.CreateFromFile(mapped, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite))
            using (var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
            {
                for (long i = 0; i < view.Capacity; i += 512)
                {
                    view.Write(i, (byte)(view.ReadByte(i) ^ 0x5A));
                }

                view.Flush();
            }

            mappedWrites++;

            string canary = Path.Combine(canaries, $"canary_{pass % Work.CanaryCount:D2}.txt");
            if (File.Exists(canary))
            {
                byte[] content = File.ReadAllBytes(canary);           // pattern 3: read,
                for (int i = 0; i < content.Length; i++) content[i] ^= 0x5A; // "encrypt" in memory,
                File.WriteAllBytes(canary, content);                  // rewrite in place
                rewrites++;
                string locked = canary + ".locked";
                File.Move(canary, locked, overwrite: true);           // pattern 4: rename
                renames++;
                File.Delete(locked);                                  // pattern 5: delete
                deletes++;
                File.WriteAllBytes(canary, content);                  // put a canary back
            }

            pass++;
        }

        Console.WriteLine($"copies={copies} rewrites={rewrites} renames={renames} deletes={deletes} mapped={mappedWrites} passes={pass}");
        return 0;
    }
}

/// <summary>
/// The concurrent reader (ADR-025, correction A): opens every canary for reading, in a loop,
/// sharing everything -- as an antivirus, the Windows indexer or a backup does. Its opens are
/// Creates too: they must never attribute.
/// </summary>
internal static class ConcurrentReader
{
    public static int Run(string root)
    {
        string canaries = Path.Combine(root, "canaries");
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                foreach (string file in Directory.EnumerateFiles(canaries))
                {
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        _ = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // renamed or deleted under us
                    }
                }

                Thread.Sleep(5);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }
}

/// <summary>What happens without administrator rights (B.2 point 5).</summary>
internal static class NoAdmin
{
    public static int Run()
    {
        Console.WriteLine($"elevated: {TraceEventSession.IsElevated() == true}");
        try
        {
            using var session = new TraceEventSession("RansomGuard-EtwProbe-NoAdmin");
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit);
            Console.WriteLine("kernel FileIO session: OPENED (unexpected without administrator rights)");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"kernel FileIO session: REFUSED -- {ex.GetType().Name}: {ex.Message}");
        }

        // The fallback SENTINEL already has: a FileSystemWatcher, which needs no privilege.
        Work.Create();
        using var watcher = new FileSystemWatcher(Work.Canaries)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        var fired = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, e) => fired.TrySetResult($"{e.ChangeType} {e.Name}");
        watcher.EnableRaisingEvents = true;
        var clock = Stopwatch.StartNew();
        File.AppendAllText(Work.CanaryPath(0), "modified");
        bool got = fired.Task.Wait(TimeSpan.FromSeconds(5));
        Console.WriteLine(got
            ? $"FileSystemWatcher fallback: event '{fired.Task.Result}' after {clock.ElapsedMilliseconds} ms -- detection without attribution"
            : "FileSystemWatcher fallback: NO event within 5 s");
        watcher.Dispose();
        Work.Clean();
        return got ? 0 : 1;
    }
}

/// <summary>The counters of one phase (rest or copy) of one configuration.</summary>
internal sealed class PhaseStats
{
    public required string Config { get; init; }
    public required string Phase { get; init; }
    public double Seconds;
    public long AllEvents;
    public readonly Dictionary<string, long> ByType = new(StringComparer.Ordinal);
    // Per FileIO event type: how many carry a resolved file name.
    public readonly Dictionary<string, long> WithName = new(StringComparer.Ordinal);
    // Mutating operations on the canaries (Write/Delete/Rename/SetInfo): the writer's PID or not.
    public readonly Dictionary<string, Dictionary<string, long>> CanaryPid = new(StringComparer.Ordinal);
    public long HeldWrites;            // writes on a handle opened BEFORE the session
    public long HeldWritesNamed;       // ... whose file name was resolved
    public readonly List<double> DeliveryMs = [];
    public double ProbeCpuSeconds;
    public double? AgentCpuSeconds;
    public int EventsLost;
    public string WriterSummary = "";
    public long Flushes;
    // Pattern 7, memory-mapped writes: every operation on the mapped canaries, by PID class.
    public readonly Dictionary<string, Dictionary<string, long>> MappedPid = new(StringComparer.Ordinal);
    // Correction A: every Create on a canary, by disposition (raw NT value = CreateOptions >> 24,
    // and TraceEvent's reading of it) and by PID class.
    public readonly Dictionary<string, Dictionary<string, long>> CreateByDisposition = new(StringComparer.Ordinal);
    // 2b: the writer's start time as the Process/Start event gives it, and as the OS gives it.
    public DateTime? WriterStartFromEtw;
    public DateTime? WriterStartFromOs;
}

internal static class Measure
{
    private const string SessionName = "RansomGuard-EtwProbe-FileIO";

    private const KernelTraceEventParser.Keywords Chosen =
        KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.DiskFileIO;

    // The keyword sets of the first run (FileIOInit alone, +FileIO) are in ADR-025; this run measures
    // the chosen set, its Flush() cadence (delivery delay against CPU), and the Process keyword
    // that gives the (PID, process start time) pair.
    private static readonly (string Name, KernelTraceEventParser.Keywords Keywords, int FlushMs)[] Configs =
    [
        ("FileIOInit+DiskFileIO", Chosen, 0),
        ("FileIOInit+DiskFileIO, Flush 250 ms", Chosen, 250),
        ("FileIOInit+DiskFileIO, Flush 100 ms", Chosen, 100),
        ("FileIOInit+DiskFileIO+Process", Chosen | KernelTraceEventParser.Keywords.Process, 0),
    ];

    private static PhaseStats? _current;
    private static int _writerPid;
    private static int _readerPid = -1;
    private static int _agentPid = -1;
    private static readonly string[] PidClasses = ["writer", "reader", "agent", "System (4)", "none (<= 0)", "other"];
    private static DateTime _writerStartOs;
    // Process start times seen in the session (Process/Start), by PID.
    private static readonly ConcurrentDictionary<int, DateTime> ProcessStarts = new();
    private static readonly List<string> SeenNames = [];
    private static string _heldPath = "";

    public static int Run(string[] args)
    {
        int seconds = IntArg(args, "--seconds") ?? 60;
        int? agentPid = IntArg(args, "--agent-pid");
        _agentPid = agentPid ?? -1;
        string? reportPath = StringArg(args, "--report");

        if (TraceEventSession.IsElevated() != true)
        {
            Console.Error.WriteLine("measure needs administrator rights (kernel session). Run `no-admin` to see the fallback.");
            return 3;
        }

        Work.Create();
        _heldPath = Path.Combine(Work.Canaries, "held_before_session.txt");
        // A canary whose handle is opened BEFORE any session starts: is its name known afterwards?
        using var held = new FileStream(_heldPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

        var results = new List<PhaseStats>();
        Console.WriteLine($"ETW FileIO probe: {Environment.MachineName}, {Environment.ProcessorCount} logical CPUs, {seconds} s per phase");

        // Baseline: no session -- what the agent costs today.
        results.Add(RunPhase("none (baseline)", "rest", seconds, agentPid, held, copy: false));
        results.Add(RunPhase("none (baseline)", "copy", seconds, agentPid, held, copy: true));

        foreach ((string name, KernelTraceEventParser.Keywords keywords, int flushMs) in Configs)
        {
            using var session = new TraceEventSession(SessionName) { BufferSizeMB = 64, StopOnDispose = true };
            session.EnableKernelProvider(keywords);
            ProcessStarts.Clear();
            Subscribe(session.Source);
            var processing = new Thread(() => session.Source.Process()) { IsBackground = true };
            processing.Start();
            Thread.Sleep(2000); // let the session settle (rundown) before measuring

            // Flush() on a timer of this process: its CPU is counted in the probe's.
            long flushes = 0;
            using Timer? flusher = flushMs > 0
                ? new Timer(_ =>
                {
                    try
                    {
                        session.Flush();
                        Interlocked.Increment(ref flushes);
                    }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                    {
                        // the session is stopping
                    }
                }, null, flushMs, flushMs)
                : null;

            // EventsLost is cumulative and only readable while the session runs.
            PhaseStats rest = RunPhase(name, "rest", seconds, agentPid, held, copy: false);
            rest.EventsLost = Lost(session);
            PhaseStats copy = RunPhase(name, "copy", seconds, agentPid, held, copy: true);
            int lostAtEnd = Lost(session);
            copy.EventsLost = lostAtEnd < 0 || rest.EventsLost < 0 ? -1 : lostAtEnd - rest.EventsLost;
            flusher?.Dispose();
            Thread.Sleep(1500); // the writer's last events and its Process/Start
            Interlocked.Exchange(ref _current, null);
            rest.Flushes = copy.Flushes = Interlocked.Read(ref flushes);
            if ((keywords & KernelTraceEventParser.Keywords.Process) != 0)
            {
                copy.WriterStartFromEtw = ProcessStarts.TryGetValue(_writerPid, out DateTime seen) ? seen : null;
                copy.WriterStartFromOs = _writerStartOs;
            }

            session.Stop();
            processing.Join(TimeSpan.FromSeconds(10));
            results.Add(rest);
            results.Add(copy);
        }

        held.Dispose();
        string report = Report(results, seconds, FilterCostNs());
        Console.WriteLine(report);
        if (reportPath is not null) File.WriteAllText(reportPath, report);
        string? summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (summary is not null) File.AppendAllText(summary, report);
        try { Work.Clean(); } catch (IOException) { }
        return 0;
    }

    private static PhaseStats RunPhase(string config, string phase, int seconds, int? agentPid, FileStream held, bool copy)
    {
        var stats = new PhaseStats { Config = config, Phase = phase };
        using Process self = Process.GetCurrentProcess();
        Process? agent = agentPid is int pid ? AgentProcess(pid) : null;
        double probeStart = Cpu(self), agentStart = agent is null ? 0 : Cpu(agent);
        var clock = Stopwatch.StartNew();
        Interlocked.Exchange(ref _current, stats);

        Process? writer = null;
        if (copy)
        {
            writer = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"writer \"{Work.Root}\" {seconds}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            _writerPid = writer.Id;
            _writerStartOs = writer.StartTime; // GetProcessTimes: what an immediate read would give
        }

        Process? reader = null;
        if (copy)
        {
            reader = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "reader", Work.Root },
                UseShellExecute = false,
            })!;
            _readerPid = reader.Id;
        }

        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(500);
            if (copy)
            {
                held.Write(Encoding.ASCII.GetBytes("tick\n")); // the pre-opened handle, written by this process
                held.Flush(flushToDisk: true);
            }
        }

        if (writer is not null)
        {
            stats.WriterSummary = writer.StandardOutput.ReadToEnd().Trim();
            writer.WaitForExit();
        }

        if (reader is not null)
        {
            reader.Kill();
            reader.WaitForExit();
            reader.Dispose();
        }

        Interlocked.Exchange(ref _current, null);
        stats.Seconds = clock.Elapsed.TotalSeconds;
        stats.ProbeCpuSeconds = Cpu(self) - probeStart;
        stats.AgentCpuSeconds = agent is null || agent.HasExited ? null : Cpu(agent) - agentStart;
        Console.WriteLine($"  {config,-30} {phase,-5} events {stats.AllEvents,10:N0}  probe CPU {stats.ProbeCpuSeconds:F2} s  agent CPU {(stats.AgentCpuSeconds is double a ? $"{a:F2} s" : "n/a")}");
        return stats;
    }

    private static int Lost(TraceEventSession session)
    {
        try
        {
            return session.EventsLost;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return -1; // unreadable: reported as such, never as 0
        }
    }

    /// <summary>The agent under measurement; null (reported "n/a") if it is not running.</summary>
    private static Process? AgentProcess(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"  agent PID {pid} is not running: its CPU is reported n/a");
            return null;
        }
    }

    private static double Cpu(Process process)
    {
        process.Refresh();
        return process.TotalProcessorTime.TotalSeconds;
    }

    private static void Subscribe(ETWTraceEventSource source)
    {
        source.AllEvents += e =>
        {
            PhaseStats? s = _current;
            if (s is null) return;
            s.AllEvents++;
            string type = $"{e.TaskName}/{e.OpcodeName}";
            s.ByType[type] = s.ByType.GetValueOrDefault(type) + 1;
            if (s.AllEvents % 97 == 0) s.DeliveryMs.Add((DateTime.Now - e.TimeStamp).TotalMilliseconds);
        };

        KernelTraceEventParser k = source.Kernel;
        k.FileIOCreate += e =>
        {
            Named("Create", e.FileName, e.ProcessID, mutating: false);
            if (_current is { Phase: "copy" } s && IsCanary(e.FileName))
            {
                // TraceEvent's CreateDisposition: Win32 names, NT values (CREATE_ALWAYS = 5 =
                // FILE_OVERWRITE_IF, OPEN_EXISTING = 1 = FILE_OPEN). PayloadByName("CreateOptions")
                // is already masked: its high byte reads 0 (run 38813859055).
                string disposition = $"{(int)e.CreateDisposition} ({e.CreateDisposition})";
                Tally(s.CreateByDisposition, disposition, e.ProcessID);
            }
        };
        k.FileIORead += e => Named("Read", e.FileName, e.ProcessID, mutating: false);
        k.FileIOWrite += e =>
        {
            Named("Write", e.FileName, e.ProcessID, mutating: true);
            if (string.Equals(e.FileName, _heldPath, StringComparison.OrdinalIgnoreCase) || e.FileName.Length == 0)
            {
                // Writes from this process on the pre-opened handle; unnamed writes counted as candidates.
                if (e.ProcessID == Environment.ProcessId && _current is { } s)
                {
                    s.HeldWrites++;
                    if (e.FileName.Length > 0) s.HeldWritesNamed++;
                }
            }
        };
        k.FileIODelete += e => Named("Delete", e.FileName, e.ProcessID, mutating: true);
        k.FileIORename += e => Named("Rename", e.FileName, e.ProcessID, mutating: true);
        k.FileIOSetInfo += e => Named("SetInfo", e.FileName, e.ProcessID, mutating: true);
        k.FileIOCleanup += e => Named("Cleanup", e.FileName, e.ProcessID, mutating: false);
        k.FileIOClose += e => Named("Close", e.FileName, e.ProcessID, mutating: false);
        k.ProcessStart += e => ProcessStarts[e.ProcessID] = e.TimeStamp;
    }

    private static string PidClass(int pid) => pid switch
    {
        4 => "System (4)",
        <= 0 => "none (<= 0)",
        _ when pid == _writerPid => "writer",
        _ when pid == _readerPid => "reader",
        _ when pid == _agentPid => "agent",
        _ => "other",
    };

    private static void Tally(Dictionary<string, Dictionary<string, long>> table, string row, int pid)
    {
        if (!table.TryGetValue(row, out Dictionary<string, long>? counts))
        {
            counts = new Dictionary<string, long>(StringComparer.Ordinal);
            table[row] = counts;
        }

        string pidClass = PidClass(pid);
        counts[pidClass] = counts.GetValueOrDefault(pidClass) + 1;
    }

    private static bool IsCanary(string fileName) =>
        fileName.StartsWith(Work.Canaries, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(fileName, _heldPath, StringComparison.OrdinalIgnoreCase);

    private static void Named(string op, string fileName, int pid, bool mutating)
    {
        PhaseStats? s = _current;
        if (s is null) return;
        if (fileName.Length > 0)
        {
            s.WithName[op] = s.WithName.GetValueOrDefault(op) + 1;
            if (SeenNames.Count < 200_000) SeenNames.Add(fileName);
        }

        if (s.Phase != "copy" || fileName.Length == 0) return;
        if (!fileName.StartsWith(Work.Canaries, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(fileName, _heldPath, StringComparison.OrdinalIgnoreCase)) return;

        if (Path.GetFileName(fileName).StartsWith(Work.MappedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Tally(s.MappedPid, op, pid); // every operation
            return;
        }

        if (mutating) Tally(s.CanaryPid, op, pid);
    }

    /// <summary>Cost of the canary filter alone, per event, on the names actually seen.</summary>
    private static double FilterCostNs()
    {
        if (SeenNames.Count == 0) return double.NaN;
        var canaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Work.CanaryCount; i++) canaries.Add(Work.CanaryPath(i));
        string[] directories = [Work.Canaries + Path.DirectorySeparatorChar];
        long hits = 0;
        var clock = Stopwatch.StartNew();
        const int Rounds = 20;
        for (int r = 0; r < Rounds; r++)
        {
            foreach (string name in SeenNames)
            {
                // What SENTINEL would do per event: a directory prefix test, then an exact lookup.
                foreach (string d in directories)
                {
                    if (name.StartsWith(d, StringComparison.OrdinalIgnoreCase) && canaries.Contains(name)) hits++;
                }
            }
        }

        GC.KeepAlive(hits);
        return clock.Elapsed.TotalMilliseconds * 1_000_000 / ((double)SeenNames.Count * Rounds);
    }

    private static string Report(List<PhaseStats> results, int seconds, double filterNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"## ETW FileIO probe -- {Environment.MachineName}, {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} logical CPUs");
        sb.AppendLine();
        sb.AppendLine($"{seconds} s per phase. CPU as % of one core (and of the whole machine).");
        sb.AppendLine();
        sb.AppendLine("| Keywords | Phase | Events/s (all) | Probe CPU | Agent CPU | Events lost | Delivery delay p50 / p95 (ms) | Flushes |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (PhaseStats s in results)
        {
            double probe = s.ProbeCpuSeconds / s.Seconds * 100;
            string agent = s.AgentCpuSeconds is double a ? $"{a / s.Seconds * 100:F1} % ({a / s.Seconds * 100 / Environment.ProcessorCount:F2} %)" : "n/a";
            string delivery = s.DeliveryMs.Count == 0 ? "--" : $"{Percentile(s.DeliveryMs, 0.5):F0} / {Percentile(s.DeliveryMs, 0.95):F0}";
            sb.AppendLine($"| {s.Config} | {s.Phase} | {s.AllEvents / s.Seconds:N0} | {probe:F1} % ({probe / Environment.ProcessorCount:F2} %) | {agent} | {s.EventsLost} | {delivery} | {s.Flushes} |");
        }

        sb.AppendLine();
        sb.AppendLine("Writer (copy phases): " + string.Join("; ", results.Where(r => r.WriterSummary.Length > 0).Select(r => $"{r.Config}: {r.WriterSummary}")));
        sb.AppendLine();
        sb.AppendLine("### Event types, copy phase (events/s), and the share carrying a resolved file name");
        sb.AppendLine();
        foreach (PhaseStats s in results.Where(r => r.Phase == "copy" && r.AllEvents > 0))
        {
            sb.AppendLine($"**{s.Config}**");
            sb.AppendLine();
            sb.AppendLine("| Event | Events/s | With file name |");
            sb.AppendLine("|---|---|---|");
            foreach ((string type, long count) in s.ByType.OrderByDescending(p => p.Value).Take(16))
            {
                string op = type.Contains('/', StringComparison.Ordinal) ? type[(type.IndexOf('/', StringComparison.Ordinal) + 1)..] : type;
                string named = s.WithName.TryGetValue(op, out long n) && type.StartsWith("FileIO", StringComparison.OrdinalIgnoreCase) ? $"{n * 100.0 / count:F0} %" : "--";
                sb.AppendLine($"| {type} | {count / s.Seconds:N1} | {named} |");
            }

            sb.AppendLine();
            Pivot(sb, "Mutating operations on the canaries, by the PID the event carries (writer = the process that did it; reader = the concurrent reader):", "Operation", s.CanaryPid);
            Pivot(sb, "Creates (opens) on every canary, by NT disposition (TraceEvent's value and name) and by PID:", "Disposition", s.CreateByDisposition);

            sb.AppendLine();
            Pivot(sb, "Pattern 7, memory-mapped rewrite of a canary: every operation on the mapped canaries, by PID:", "Operation", s.MappedPid);

            sb.AppendLine();
            sb.AppendLine($"Handle opened before the session, written during it: {s.HeldWrites} writes seen, {s.HeldWritesNamed} with the file name resolved.");
            if (s.WriterStartFromOs is DateTime os)
            {
                sb.AppendLine(s.WriterStartFromEtw is DateTime etw
                    ? $"Writer start time: Process/Start {etw:HH:mm:ss.fff}, OS (GetProcessTimes) {os:HH:mm:ss.fff}, difference {(etw - os).TotalMilliseconds:F1} ms."
                    : $"Writer start time: no Process/Start event seen for PID {_writerPid}; OS {os:HH:mm:ss.fff}.");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"Canary filter (directory prefix + exact lookup), per event, on the names seen: {filterNs:F0} ns.");
        return sb.ToString();
    }

    private static void Pivot(StringBuilder sb, string title, string rowName, Dictionary<string, Dictionary<string, long>> table)
    {
        sb.AppendLine(title);
        sb.AppendLine();
        sb.AppendLine($"| {rowName} | " + string.Join(" | ", PidClasses) + " |");
        sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", PidClasses.Length)));
        foreach ((string row, Dictionary<string, long> counts) in table.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {row} | " + string.Join(" | ", PidClasses.Select(c => counts.GetValueOrDefault(c).ToString(CultureInfo.InvariantCulture))) + " |");
        }

        sb.AppendLine();
    }

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(p * sorted.Count))];
    }

    private static int? IntArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : null;
    }

    private static string? StringArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
