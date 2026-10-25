using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The cache answers "who wrote this file" after the writer has closed its handle, which is what
/// ENTROPY needs and what the Restart Manager cannot do (AGT-ATTR-001).
///
/// The rules are deliberately the same as <see cref="CanaryAttributor"/>'s: a wrong PID is worse
/// than no PID. These tests pin each refusal, because every one of them is a case where the console
/// must say "process not identified" rather than name somebody.
/// </summary>
public sealed class FileWriteAttributionCacheTests
{
    private const string WatchedDir = @"C:\Watched";
    private static readonly string WatchedFile = Path.Combine(WatchedDir, "patient.docx");

    // ManualTimeProvider starts at this instant; the cache's retention is measured from it.
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static FileWriteAttributionCache Cache(
        ManualTimeProvider time,
        Func<int, ProcessFacts?>? readProcess = null,
        int agentPid = 999)
    {
        // Default: every PID is a process born well before the writes, i.e. a valid attribution.
        readProcess ??= pid => new ProcessFacts(Now.AddHours(-1), $"writer{pid}", $@"C:\bin\writer{pid}.exe");
        var cache = new FileWriteAttributionCache(new ProcessCreationTimes(readProcess), time, agentPid);
        cache.SetWatchedDirectories([WatchedDir]);
        return cache;
    }

    private static FileIoRecord Write(string path, int pid, DateTime at) =>
        new(FileIoOperation.Write, path, pid, at, 0, 0);

    private static ManualTimeProvider Clock() => new();

    [Fact]
    public void A_write_then_a_lookup_names_the_writer()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(WatchedFile, 4521, Now));
        // ENTROPY reads and hashes the file before asking: the writer has long closed its handle.
        time.Advance(TimeSpan.FromSeconds(5));

        AttributionOutcome outcome = cache.Resolve(WatchedFile, etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Resolved);
        outcome.ProcessId.ShouldBe(4521);
        outcome.ProcessName.ShouldBe("writer4521");
        outcome.StateCode.ShouldBe("resolved");
    }

    [Fact]
    public void Two_writers_in_the_window_are_ambiguous_and_name_nobody()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(WatchedFile, 4521, Now));
        cache.OnFileIo(Write(WatchedFile, 7777, Now.AddSeconds(1)));

        AttributionOutcome outcome = cache.Resolve(WatchedFile, etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Ambiguous);
        outcome.ProcessId.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void The_system_pids_never_attribute(int systemPid)
    {
        // PID 4 carries the system's deferred and mapped-page writes: it is not the author.
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(WatchedFile, systemPid, Now));

        cache.Recorded.ShouldBe(0);
        cache.Resolve(WatchedFile, etwRunning: true).Reason.ShouldBe(AttributionReason.NoWriteEvent);
    }

    [Fact]
    public void The_agents_own_writes_never_attribute()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time, agentPid: 4242);

        cache.OnFileIo(Write(WatchedFile, 4242, Now));

        cache.Recorded.ShouldBe(0);
        cache.Resolve(WatchedFile, etwRunning: true).Reason.ShouldBe(AttributionReason.NoWriteEvent);
    }

    [Fact]
    public void A_pid_whose_process_was_born_after_the_write_was_reused()
    {
        ManualTimeProvider time = Clock();
        // The process now holding PID 4521 started a minute after the write: not the writer.
        FileWriteAttributionCache cache = Cache(time, readProcess: _ => new ProcessFacts(Now.AddMinutes(1), "later", null));

        cache.OnFileIo(Write(WatchedFile, 4521, Now));

        AttributionOutcome outcome = cache.Resolve(WatchedFile, etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe(AttributionReason.PidReused);
    }

    [Fact]
    public void A_writer_that_has_exited_and_cannot_be_read_is_unavailable()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time, readProcess: _ => null);

        cache.OnFileIo(Write(WatchedFile, 4521, Now));

        AttributionOutcome outcome = cache.Resolve(WatchedFile, etwRunning: true);

        outcome.State.ShouldBe(AttributionState.Unavailable);
        outcome.Reason.ShouldBe(AttributionReason.ProcessExited);
    }

    [Fact]
    public void A_write_older_than_the_retention_no_longer_answers()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(WatchedFile, 4521, Now));
        time.Advance(FileWriteAttributionCache.Retention + TimeSpan.FromSeconds(1));

        cache.Resolve(WatchedFile, etwRunning: true).Reason.ShouldBe(AttributionReason.NoWriteEvent);
    }

    [Fact]
    public void A_session_that_is_not_running_says_so_rather_than_no_write_event()
    {
        // The difference matters: "etw_unavailable" means the agent could not see, and
        // "no_write_event" means it saw and there was nothing. The console shows them differently.
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.Resolve(WatchedFile, etwRunning: false).Reason.ShouldBe(AttributionReason.EtwUnavailable);
    }

    [Fact]
    public void A_file_outside_the_watched_folders_is_never_kept()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(@"C:\Elsewhere\other.docx", 4521, Now));

        cache.TrackedPaths.ShouldBe(0);
        cache.Recorded.ShouldBe(0);
    }

    [Fact]
    public void An_opening_for_reading_is_not_a_write_and_is_not_kept()
    {
        // FILE_OPEN (disposition 1): antivirus and indexers do this constantly.
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(new FileIoRecord(FileIoOperation.Create, WatchedFile, 4521, Now, CreateDisposition: 1, InfoClass: 0));

        cache.Recorded.ShouldBe(0);
    }

    [Fact]
    public void The_path_is_matched_whatever_the_case_the_kernel_reports()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.OnFileIo(Write(WatchedFile.ToUpperInvariant(), 4521, Now));

        cache.Resolve(WatchedFile.ToLowerInvariant(), etwRunning: true).ProcessId.ShouldBe(4521);
    }

    [Fact]
    public void A_file_never_written_answers_no_write_event()
    {
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        cache.Resolve(Path.Combine(WatchedDir, "untouched.docx"), etwRunning: true)
            .Reason.ShouldBe(AttributionReason.NoWriteEvent);
    }

    [Fact]
    public void The_cache_stays_bounded_under_a_flood()
    {
        // A workstation copying a folder generates far more writes than the cap; the cache must
        // drop the oldest paths rather than grow with the machine's file activity.
        ManualTimeProvider time = Clock();
        FileWriteAttributionCache cache = Cache(time);

        for (int i = 0; i < FileWriteAttributionCache.Capacity + 5_000; i++)
        {
            cache.OnFileIo(Write(Path.Combine(WatchedDir, $"file{i}.tmp"), 4521, Now));
            time.Advance(TimeSpan.FromMilliseconds(1));
        }

        cache.TrackedPaths.ShouldBeLessThanOrEqualTo(FileWriteAttributionCache.Capacity);
        cache.Dropped.ShouldBeGreaterThan(0);
        // The most recent write still answers: trimming drops the oldest, never the newest.
        cache.Resolve(Path.Combine(WatchedDir, $"file{FileWriteAttributionCache.Capacity + 4_999}.tmp"), etwRunning: true)
            .State.ShouldBe(AttributionState.Resolved);
    }
}
