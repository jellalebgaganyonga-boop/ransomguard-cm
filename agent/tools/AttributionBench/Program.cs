// AttributionBench — measures how often SENTINEL's process attribution (Restart Manager)
// names the process that touched a canary.
//
// It is a measurement instrument for a defensive product. It only ever touches files it
// creates itself, in its own isolated temporary directory (%TEMP%\rg-attribution-bench),
// which it deletes at the end. See README.md.
//
// Usage: dotnet run --project agent/tools/AttributionBench -c Release -- [iterations] [consumerDelayMs]
//   (a concurrent reader process -- the bench itself, "reader <dir>" -- runs during every pattern)
//   iterations      per pattern, default 20
//   consumerDelayMs delay before the attribution query, standing in for SENTINEL's repository
//                   lookup between the event and the query; default 10

using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.Sentinel;

if (args.Length > 1 && args[0] == "reader")
{
    return Reader.Run(args[1]);
}

int iterations = args.Length > 0 ? int.Parse(args[0]) : 20;
int consumerDelayMs = args.Length > 1 ? int.Parse(args[1]) : 10;
int benchPid = Environment.ProcessId;

string dir = Path.Combine(Path.GetTempPath(), "rg-attribution-bench");
if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
Directory.CreateDirectory(dir);

Console.WriteLine("SENTINEL attribution bench");
Console.WriteLine($"  directory        : {dir}");
Console.WriteLine($"  iterations       : {iterations} per pattern");
Console.WriteLine($"  consumer delay   : {consumerDelayMs} ms (stands in for the repository lookup)");
Console.WriteLine($"  bench PID        : {benchPid}  <- the PID attribution must return");
Console.WriteLine();

var restartManager = new RestartManagerHelper(NullLogger<RestartManagerHelper>.Instance);

// Same pipeline as SentinelMonitor: watcher -> bounded channel (10 000, DropOldest) -> consumer
// -> Restart Manager query -> most recently started holder.
var channel = Channel.CreateBounded<WatcherEvent>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
});
var pending = new Dictionary<string, TaskCompletionSource<Attempt>>(StringComparer.OrdinalIgnoreCase);
var pendingLock = new object();

using var watcher = new FileSystemWatcher(dir)
{
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, // as SentinelMonitor
    IncludeSubdirectories = false,
};
watcher.Changed += (_, e) => channel.Writer.TryWrite(new WatcherEvent(e.FullPath, "Modified", DateTime.UtcNow));
watcher.Deleted += (_, e) => channel.Writer.TryWrite(new WatcherEvent(e.FullPath, "Deleted", DateTime.UtcNow));
// SENTINEL looks the canary up by its original path, so the query targets the old name.
watcher.Renamed += (_, e) => channel.Writer.TryWrite(new WatcherEvent(e.OldFullPath, "Renamed", DateTime.UtcNow));
watcher.EnableRaisingEvents = true;

Task consumer = Task.Run(async () =>
{
    await foreach (WatcherEvent evt in channel.Reader.ReadAllAsync())
    {
        TaskCompletionSource<Attempt>? tcs;
        lock (pendingLock) { pending.TryGetValue(evt.Path, out tcs); }
        if (tcs is null || tcs.Task.IsCompleted) continue; // not the file under measurement

        if (consumerDelayMs > 0) await Task.Delay(consumerDelayMs);
        IReadOnlyList<ProcessAttribution> holders = restartManager.GetProcessesLockingFile(evt.Path);
        // Check 1: the independent reference query, right after the production one.
        ReferenceResult reference = ReferenceRestartManager.Holders(evt.Path);
        ProcessAttribution? offender = holders
            .OrderByDescending(p => p.StartTime ?? DateTime.MinValue) // as SentinelMonitor
            .FirstOrDefault();
        tcs.TrySetResult(new Attempt(evt.Kind, evt.SeenAt, holders.Count, offender?.ProcessId, reference));
    }
});

Pattern[] patterns =
[
    new(1, "Open, write, hold handle 5 s, close", "Modified", async path =>
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        await fs.WriteAsync(RandomNumberGenerator.GetBytes(4096));
        await fs.FlushAsync();
        await Task.Delay(5000);
    }),
    new(2, "Open, write, close immediately", "Modified", async path =>
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        await fs.WriteAsync(RandomNumberGenerator.GetBytes(4096));
    }),
    new(3, "Read, encrypt in memory, rewrite, close (fast)", "Modified", async path =>
    {
        byte[] plain = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, EncryptOwnTestData(plain));
    }),
    new(4, "Rename to .locked", "Renamed", path =>
    {
        File.Move(path, path + ".locked");
        return Task.CompletedTask;
    }),
    new(5, "Delete the canary", "Deleted", path =>
    {
        File.Delete(path);
        return Task.CompletedTask;
    }),
    new(6, "Write encrypted copy, then delete the original", "Deleted", async path =>
    {
        byte[] plain = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path + ".locked", EncryptOwnTestData(plain));
        File.Delete(path);
    }),
    // Pattern 7: rewrite through a memory-mapped view (ADR-025, 4d), with an explicit flush (7a)
    // and without (7b): the pages then leave later, written by the system's mapped page writer.
    new(7, "Rewrite through a mapped view, flushed", "Modified", path =>
    {
        RewriteMapped(path, flush: true);
        return Task.CompletedTask;
    }),
    new(8, "Rewrite through a mapped view, not flushed", "Modified", path =>
    {
        RewriteMapped(path, flush: false);
        return Task.CompletedTask;
    }),
];

// The concurrent reader (ADR-025, correction A): another process opens every file of the bench
// directory for reading, in a loop, sharing everything -- as an antivirus, the Windows indexer or
// a backup does on a real workstation. Its PID is never the right answer.
using Process reader = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
{
    ArgumentList = { "reader", dir },
    UseShellExecute = false,
})!;
int readerPid = reader.Id;
Console.WriteLine($"  concurrent reader: PID {readerPid}  <- never the right answer");
Console.WriteLine();

// Check 2: the owner-thread probe. The bench queries the Restart Manager about a file from the
// thread that holds it open for writing. If neither query sees the bench here, the environment
// (rights, session, file location) is blind and any 0 % below is meaningless.
const int OwnerProbes = 5;
int ownerSeenByProduction = 0, ownerSeenByReference = 0;
var ownerReferenceErrors = new List<int>();
string ownerProbe = Path.Combine(dir, "owner_thread_probe.txt");
for (int i = 0; i < OwnerProbes; i++)
{
    using var held = new FileStream(ownerProbe, FileMode.Create, FileAccess.Write, FileShare.Read);
    held.Write(RandomNumberGenerator.GetBytes(4096));
    held.Flush();
    if (restartManager.GetProcessesLockingFile(ownerProbe).Any(p => p.ProcessId == benchPid)) ownerSeenByProduction++;
    ReferenceResult owner = ReferenceRestartManager.Holders(ownerProbe);
    if (owner.Failed) ownerReferenceErrors.Add(owner.Error);
    else if (owner.Pids.Contains(benchPid)) ownerSeenByReference++;
}

File.Delete(ownerProbe);
Console.WriteLine($"Check 2, owner-thread probe (handle held by this thread): production saw the bench {ownerSeenByProduction}/{OwnerProbes}, reference {ownerSeenByReference}/{OwnerProbes}"
    + (ownerReferenceErrors.Count > 0 ? $", reference errors {string.Join(",", ownerReferenceErrors)}" : ""));
Console.WriteLine();

var results = new List<PatternResult>();
foreach (Pattern pattern in patterns)
{
    Console.Write($"[{pattern.Label}] {pattern.Name} ");
    var outcome = new PatternResult(pattern);
    for (int i = 0; i < iterations; i++)
    {
        string path = Path.Combine(dir, $"0001_dossier_patient_p{pattern.Number}_{i:00}.txt");
        // With the concurrent reader, creating the canary once failed (2026-10-15, message lost):
        // three attempts, then the iteration counts as an action failure, with its message.
        Exception? createFailure = null;
        for (int creation = 0; creation < 3; creation++)
        {
            try
            {
                await File.WriteAllTextAsync(path, $"Dossier patient synthetique {i} — fichier de mesure du banc\n");
                createFailure = null;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                createFailure = ex;
                await Task.Delay(50);
            }
        }

        if (createFailure is not null)
        {
            Console.Write('x');
            outcome.ActionFailures.Add($"canary creation: {createFailure.GetType().Name}: {createFailure.Message}");
            continue;
        }
        await Task.Delay(400); // let the creation events go by before the file is under measurement

        var tcs = new TaskCompletionSource<Attempt>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (pendingLock) { pending[path] = tcs; }

        DateTime handleReleased;
        try
        {
            await pattern.Act(path);
            // Every pattern has released the file by the time Act returns.
            handleReleased = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.Write('x');
            outcome.ActionFailures.Add(ex.Message);
            continue;
        }

        Task finished = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        lock (pendingLock) { pending.Remove(path); }
        if (finished != tcs.Task)
        {
            Console.Write('_');
            outcome.NoEvent++;
            continue;
        }

        Attempt attempt = tcs.Task.Result;
        if (attempt.Reference.Failed) outcome.ReferenceErrors++;
        else if (attempt.Reference.Pids.Count > 0 && attempt.Holders == 0) outcome.OnlyReferenceFound++;
        else if (attempt.Reference.Pids.Count == 0 && attempt.Holders > 0) outcome.OnlyProductionFound++;
        outcome.EventVsRelease.Add((attempt.SeenAt - handleReleased).TotalMilliseconds);
        if (attempt.Holders > 1) outcome.MoreThanOneHolder++;
        if (attempt.OffenderPid is null) { Console.Write('.'); outcome.NotAttributed++; }
        else if (attempt.OffenderPid == benchPid) { Console.Write('#'); outcome.CorrectPid++; }
        else
        {
            Console.Write('!');
            outcome.WrongPid++;
            if (attempt.OffenderPid == readerPid) outcome.WrongPidIsReader++;
        }
    }
    Console.WriteLine();
    results.Add(outcome);
}

watcher.EnableRaisingEvents = false;
channel.Writer.TryComplete();
await consumer;
reader.Kill();
reader.WaitForExit();
Directory.Delete(dir, recursive: true);

Console.WriteLine();
Console.WriteLine("legend: # correct PID   ! wrong PID   . no attribution   _ no event   x action failed");
Console.WriteLine();
Console.WriteLine("| # | Pattern | Iter | Attributed | Rate | Correct PID | Wrong PID | of which the reader | >1 holder |");
Console.WriteLine("|---|---------|------|------------|------|-------------|-----------|---------------------|-----------|");
foreach (PatternResult r in results)
{
    int attributed = r.CorrectPid + r.WrongPid;
    Console.WriteLine($"| {r.Pattern.Label} | {r.Pattern.Name} | {iterations} | {attributed} | {Rate(attributed, iterations),6} | {r.CorrectPid} | {r.WrongPid} | {r.WrongPidIsReader} | {r.MoreThanOneHolder} |");
}

Console.WriteLine();
Console.WriteLine("Event arrival relative to the writer releasing the handle (negative = before, i.e. still attributable):");
foreach (PatternResult r in results)
{
    string median = r.EventVsRelease.Count == 0 ? "n/a" : $"{Median(r.EventVsRelease),8:0}";
    int beforeClose = r.EventVsRelease.Count(ms => ms < 0);
    Console.WriteLine($"  pattern {r.Pattern.Label}: median {median} ms   before-close {beforeClose}/{r.EventVsRelease.Count}   no event {r.NoEvent}   action failed {r.ActionFailures.Count}");
}

foreach (PatternResult r in results.Where(r => r.ActionFailures.Count > 0))
{
    foreach (string failure in r.ActionFailures.Distinct())
    {
        Console.WriteLine($"  pattern {r.Pattern.Label} action failure: {failure}");
    }
}

Console.WriteLine();
Console.WriteLine("Check 1, reference Restart Manager query (independent of the production wrapper), per event:");
foreach (PatternResult r in results)
{
    Console.WriteLine($"  pattern {r.Pattern.Label}: only the reference found a holder {r.OnlyReferenceFound}   only production {r.OnlyProductionFound}   reference API errors {r.ReferenceErrors}");
}

int onlyReference = results.Sum(r => r.OnlyReferenceFound);
int referenceErrors = results.Sum(r => r.ReferenceErrors);
bool instrumentValid = ownerSeenByReference == OwnerProbes && ownerSeenByProduction == OwnerProbes
    && onlyReference == 0 && referenceErrors == 0;
Console.WriteLine();
Console.WriteLine(instrumentValid
    ? "Instrument checks 1 and 2: PASSED -- the production wrapper sees what Windows sees, and the environment is not blind."
    : $"Instrument checks 1 and 2: FAILED -- owner probe production {ownerSeenByProduction}/{OwnerProbes}, reference {ownerSeenByReference}/{OwnerProbes}; only-reference events {onlyReference}; reference errors {referenceErrors}. The figures below do not measure the agent.");

int decisive = results.Where(r => r.Pattern.Number is 3 or 5 or 6).Sum(r => r.CorrectPid);
int decisiveTotal = 3 * iterations;
double decisiveRate = decisiveTotal == 0 ? 0 : 100.0 * decisive / decisiveTotal;
Console.WriteLine();
Console.WriteLine($"Ransomware-shaped patterns (3, 5, 6): {decisive}/{decisiveTotal} correctly attributed = {decisiveRate:0}%");
Console.WriteLine(decisiveRate switch
{
    >= 80 => "  -> threshold >= 80%: ship the action engine on Restart Manager, ETW later as hardening",
    >= 50 => "  -> threshold 50-80%: ship, and start ETW FileIO immediately after",
    _ => "  -> threshold < 50%: attribution misses the real attacks",
});
return instrumentValid ? 0 : 4;

static string Rate(int part, int total) => total == 0 ? "n/a" : $"{100.0 * part / total:0}%";

// Patterns 7a and 7b: the canary rewritten through a memory-mapped view of the bench's own file.
static void RewriteMapped(string path, bool flush)
{
    using var file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
    using var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
    for (long i = 0; i < view.Capacity; i++)
    {
        view.Write(i, (byte)(view.ReadByte(i) ^ 0x5A));
    }

    if (flush)
    {
        view.Flush();
    }
}

static double Median(List<double> values)
{
    var sorted = values.OrderBy(v => v).ToList();
    int mid = sorted.Count / 2;
    return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

// Encrypts the bench's own synthetic file content with a throwaway key: the point is only to
// reproduce the I/O shape of an encrypting writer (read, transform, rewrite).
static byte[] EncryptOwnTestData(byte[] plain)
{
    using var aes = Aes.Create();
    aes.GenerateKey();
    aes.GenerateIV();
    return aes.EncryptCbc(plain, aes.IV);
}

internal sealed record WatcherEvent(string Path, string Kind, DateTime SeenAt);

internal sealed record Attempt(string Kind, DateTime SeenAt, int Holders, int? OffenderPid, ReferenceResult Reference);

internal sealed record Pattern(int Number, string Name, string ExpectedEvent, Func<string, Task> Act)
{
    /// <summary>"1" to "6", then "7a" (mapped, flushed) and "7b" (mapped, not flushed).</summary>
    public string Label => Number switch { 7 => "7a", 8 => "7b", _ => Number.ToString() };
}

/// <summary>The concurrent reader: opens every file of the directory for reading, in a loop.</summary>
internal static class Reader
{
    public static int Run(string directory)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        // Share everything, like an antivirus: the reader never blocks the writer.
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        _ = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // The file was renamed or deleted under us: the next pass sees the new state.
                    }
                }

                Thread.Sleep(5);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return 0; // the bench is over
        }
    }
}

internal sealed class PatternResult(Pattern pattern)
{
    public Pattern Pattern { get; } = pattern;
    public int CorrectPid { get; set; }
    public int WrongPid { get; set; }
    public int NotAttributed { get; set; }
    public int NoEvent { get; set; }
    public int MoreThanOneHolder { get; set; }
    public int WrongPidIsReader { get; set; }
    public int OnlyReferenceFound { get; set; }
    public int OnlyProductionFound { get; set; }
    public int ReferenceErrors { get; set; }
    public List<double> EventVsRelease { get; } = [];
    public List<string> ActionFailures { get; } = [];
}
