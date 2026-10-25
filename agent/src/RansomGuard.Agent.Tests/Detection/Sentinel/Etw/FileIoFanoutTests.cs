using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>
/// The fan-out exists so ENTROPY can read the same kernel FileIO session as SENTINEL
/// (AGT-ATTR-001). What matters: every subscriber gets every record, and a subscriber that throws
/// never costs another one its events -- SENTINEL's filter must keep receiving whatever else breaks.
/// </summary>
public sealed class FileIoFanoutTests
{
    private static FileIoRecord Write(string path, int pid = 1234) =>
        new(FileIoOperation.Write, path, pid, DateTime.UtcNow, 0, 0);

    [Fact]
    public void Every_subscriber_receives_every_record()
    {
        var fanout = new FileIoFanout();
        List<string> first = [];
        List<string> second = [];
        fanout.Subscribe(r => first.Add(r.FileName));
        fanout.Subscribe(r => second.Add(r.FileName));

        fanout.OnFileIo(Write(@"C:\a.txt"));
        fanout.OnFileIo(Write(@"C:\b.txt"));

        first.ShouldBe([@"C:\a.txt", @"C:\b.txt"]);
        second.ShouldBe([@"C:\a.txt", @"C:\b.txt"]);
        fanout.Dispatched.ShouldBe(2);
        fanout.SubscriberCount.ShouldBe(2);
    }

    [Fact]
    public void No_subscriber_is_not_an_error()
    {
        var fanout = new FileIoFanout();

        Should.NotThrow(() => fanout.OnFileIo(Write(@"C:\a.txt")));

        fanout.Dispatched.ShouldBe(1);
        fanout.Faults.ShouldBe(0);
    }

    [Fact]
    public void A_subscriber_that_throws_is_counted_and_the_others_still_receive()
    {
        var fanout = new FileIoFanout();
        List<string> healthy = [];
        fanout.Subscribe(_ => throw new InvalidOperationException("broken consumer"));
        fanout.Subscribe(r => healthy.Add(r.FileName));

        Should.NotThrow(() => fanout.OnFileIo(Write(@"C:\a.txt")));
        Should.NotThrow(() => fanout.OnFileIo(Write(@"C:\b.txt")));

        // The healthy subscriber -- SENTINEL's filter in production -- lost nothing.
        healthy.ShouldBe([@"C:\a.txt", @"C:\b.txt"]);
        fanout.Faults.ShouldBe(2);
    }

    [Fact]
    public void A_subscriber_registered_while_records_flow_starts_receiving_without_disturbing_the_first()
    {
        var fanout = new FileIoFanout();
        List<string> first = [];
        fanout.Subscribe(r => first.Add(r.FileName));
        fanout.OnFileIo(Write(@"C:\before.txt"));

        List<string> late = [];
        fanout.Subscribe(r => late.Add(r.FileName));
        fanout.OnFileIo(Write(@"C:\after.txt"));

        first.ShouldBe([@"C:\before.txt", @"C:\after.txt"]);
        late.ShouldBe([@"C:\after.txt"]); // it was not there for the first one
    }

    [Fact]
    public void Subscribe_refuses_null()
    {
        var fanout = new FileIoFanout();

        Should.Throw<ArgumentNullException>(() => fanout.Subscribe(null!));
    }

    [Fact]
    public async Task Subscribing_during_dispatch_never_breaks_the_hot_path()
    {
        // The session thread dispatches while another thread subscribes: the array is replaced,
        // never mutated, so a dispatch sees one set or the other and never a half-built array.
        var fanout = new FileIoFanout();
        int received = 0;
        fanout.Subscribe(_ => Interlocked.Increment(ref received));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Task dispatching = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                fanout.OnFileIo(Write(@"C:\hot.txt"));
            }
        });

        Task subscribing = Task.Run(() =>
        {
            for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
            {
                fanout.Subscribe(_ => { });
                Thread.Sleep(1);
            }
        });

        await subscribing;
        cts.Cancel();
        await Should.NotThrowAsync(dispatching);

        received.ShouldBeGreaterThan(0);
        fanout.Faults.ShouldBe(0);
    }
}
