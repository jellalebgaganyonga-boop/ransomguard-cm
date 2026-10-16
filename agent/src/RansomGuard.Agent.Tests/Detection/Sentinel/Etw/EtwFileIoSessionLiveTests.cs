using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The real kernel session (ADR-025, commit 1). Needs an administrator: the CI's Windows runner is
/// one. On a non-elevated machine the test fails and says so -- it does not pass silently; filter
/// it out with <c>--filter "Category!=Etw"</c>.
/// </summary>
[Trait("Category", "Etw")]
[Collection(nameof(EtwFileIoSessionLiveTests))] // one kernel session of this name at a time
public sealed class EtwFileIoSessionLiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rg-etw-session-" + Guid.NewGuid().ToString("N"));

    public EtwFileIoSessionLiveTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void The_session_delivers_writes_and_openings_with_this_process_PID_and_the_NT_disposition()
    {
        (TraceEventSession.IsElevated() == true).ShouldBeTrue(
            "this test opens a kernel ETW session: run it elevated, or filter Category!=Etw");
        var records = new ConcurrentQueue<FileIoRecord>();
        using var session = new EtwFileIoSession(
            () => new TraceEventKernelFileIoTrace(), NullLogger<EtwFileIoSession>.Instance);

        session.Start(r =>
        {
            if (r.FileName.StartsWith(_dir, StringComparison.OrdinalIgnoreCase))
            {
                records.Enqueue(r);
            }
        }, _ => { }).ShouldBeTrue($"the session did not start: {session.UnavailableReason}");
        Thread.Sleep(1500); // the session's rundown

        string written = Path.Combine(_dir, "written.txt");
        File.WriteAllBytes(written, new byte[4096]);         // FileMode.Create: FILE_OVERWRITE_IF (5)
        using (var read = new FileStream(written, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            _ = read.ReadByte();                              // FileMode.Open: FILE_OPEN (1)
        }

        int pid = Environment.ProcessId;
        WaitFor(records, () => records.Count(r => r.FileName.Equals(written, StringComparison.OrdinalIgnoreCase)
            && r.ProcessId == pid && r.Operation == FileIoOperation.Create) >= 2
            && records.Any(r => r.Operation == FileIoOperation.Write && r.ProcessId == pid));
        session.Stop();

        records.ShouldContain(r => r.Operation == FileIoOperation.Write && r.ProcessId == pid
            && r.FileName.Equals(written, StringComparison.OrdinalIgnoreCase));
        var creates = records.Where(r => r.Operation == FileIoOperation.Create && r.ProcessId == pid
            && r.FileName.Equals(written, StringComparison.OrdinalIgnoreCase)).Select(r => r.CreateDisposition).ToList();
        creates.ShouldContain(5u); // the write: FILE_OVERWRITE_IF
        creates.ShouldContain(1u); // the read: FILE_OPEN
        session.Flushes.ShouldBeGreaterThan(0);
        TraceEventSession.GetActiveSessionNames().ShouldNotContain(EtwFileIoSession.SessionName);
    }

    private static void WaitFor(ConcurrentQueue<FileIoRecord> records, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException(
                    $"expected records not delivered within 10 s; got: {string.Join("; ", records.Select(r => $"{r.Operation} {r.CreateDisposition} {r.ProcessId} {Path.GetFileName(r.FileName)}"))}");
            }

            Thread.Sleep(20);
        }
    }
}
