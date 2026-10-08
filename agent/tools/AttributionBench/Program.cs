// AttributionBench — measures how often SENTINEL's process attribution (Restart Manager)
// names the process that touched a canary.
//
// It is a measurement instrument for a defensive product. It only ever touches files it
// creates itself, in its own isolated temporary directory (%TEMP%\rg-attribution-bench),
// which it deletes at the end. See README.md.
//
// Usage: dotnet run --project agent/tools/AttributionBench -c Release -- [iterations] [consumerDelayMs]
//   iterations      per pattern, default 20
//   consumerDelayMs delay before the attribution query, standing in for SENTINEL's repository
//                   lookup between the event and the query; default 10

using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.Sentinel;

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
        ProcessAttribution? offender = holders
            .OrderByDescending(p => p.StartTime ?? DateTime.MinValue) // as SentinelMonitor
            .FirstOrDefault();
        tcs.TrySetResult(new Attempt(evt.Kind, evt.SeenAt, holders.Count, offender?.ProcessId));
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
];

var results = new List<PatternResult>();
foreach (Pattern pattern in patterns)
{
    Console.Write($"[{pattern.Number}/6] {pattern.Name} ");
    var outcome = new PatternResult(pattern);
    for (int i = 0; i < iterations; i++)
    {
        string path = Path.Combine(dir, $"0001_dossier_patient_p{pattern.Number}_{i:00}.txt");
        await File.WriteAllTextAsync(path, $"Dossier patient synthetique {i} — fichier de mesure du banc\n");
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
        outcome.EventVsRelease.Add((attempt.SeenAt - handleReleased).TotalMilliseconds);
        if (attempt.Holders > 1) outcome.MoreThanOneHolder++;
        if (attempt.OffenderPid is null) { Console.Write('.'); outcome.NotAttributed++; }
        else if (attempt.OffenderPid == benchPid) { Console.Write('#'); outcome.CorrectPid++; }
        else { Console.Write('!'); outcome.WrongPid++; }
    }
    Console.WriteLine();
    results.Add(outcome);
}

watcher.EnableRaisingEvents = false;
channel.Writer.TryComplete();
await consumer;
Directory.Delete(dir, recursive: true);

Console.WriteLine();
Console.WriteLine("legend: # correct PID   ! wrong PID   . no attribution   _ no event   x action failed");
Console.WriteLine();
Console.WriteLine("| # | Pattern | Iter | Attributed | Rate | Correct PID | Wrong PID | >1 holder |");
Console.WriteLine("|---|---------|------|------------|------|-------------|-----------|-----------|");
foreach (PatternResult r in results)
{
    int attributed = r.CorrectPid + r.WrongPid;
    Console.WriteLine($"| {r.Pattern.Number} | {r.Pattern.Name} | {iterations} | {attributed} | {Rate(attributed, iterations),6} | {r.CorrectPid} | {r.WrongPid} | {r.MoreThanOneHolder} |");
}

Console.WriteLine();
Console.WriteLine("Event arrival relative to the writer releasing the handle (negative = before, i.e. still attributable):");
foreach (PatternResult r in results)
{
    string median = r.EventVsRelease.Count == 0 ? "n/a" : $"{Median(r.EventVsRelease),8:0}";
    int beforeClose = r.EventVsRelease.Count(ms => ms < 0);
    Console.WriteLine($"  pattern {r.Pattern.Number}: median {median} ms   before-close {beforeClose}/{r.EventVsRelease.Count}   no event {r.NoEvent}   action failed {r.ActionFailures.Count}");
}

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
return 0;

static string Rate(int part, int total) => total == 0 ? "n/a" : $"{100.0 * part / total:0}%";

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

internal sealed record Attempt(string Kind, DateTime SeenAt, int Holders, int? OffenderPid);

internal sealed record Pattern(int Number, string Name, string ExpectedEvent, Func<string, Task> Act);

internal sealed class PatternResult(Pattern pattern)
{
    public Pattern Pattern { get; } = pattern;
    public int CorrectPid { get; set; }
    public int WrongPid { get; set; }
    public int NotAttributed { get; set; }
    public int NoEvent { get; set; }
    public int MoreThanOneHolder { get; set; }
    public List<double> EventVsRelease { get; } = [];
    public List<string> ActionFailures { get; } = [];
}
