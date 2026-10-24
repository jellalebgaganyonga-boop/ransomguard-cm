using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Etw;

/// <summary>One detection per operation (ADR-025, section 3): key, window, ETW misses.</summary>
public sealed class CanaryCorrelatorTests
{
    private const string Canary = @"C:\Patients\0001_dossier_patient.docx";
    private readonly ManualTimeProvider _time = new();
    private readonly CanaryCorrelator _correlator;

    public CanaryCorrelatorTests()
    {
        _correlator = new CanaryCorrelator(_time);
    }

    [Fact]
    public void The_watcher_first_then_ETW_in_the_window_is_one_detection_raised_by_the_watcher()
    {
        CanaryDetection? first = _correlator.OnWatcher(Canary, CanaryOperation.Modification);
        _time.Advance(TimeSpan.FromMilliseconds(150));
        CanaryDetection? second = _correlator.OnEtw(Write(Canary));

        first.ShouldNotBeNull().FirstSource.ShouldBe(SignalSource.Watcher);
        second.ShouldBeNull();
        first.SeenByEtw.ShouldBeTrue();
        first.Writes.ShouldHaveSingleItem().ProcessId.ShouldBe(4242);
    }

    [Fact]
    public void ETW_first_then_the_watcher_is_one_detection_raised_by_ETW()
    {
        CanaryDetection? first = _correlator.OnEtw(Write(Canary));
        CanaryDetection? second = _correlator.OnWatcher(Canary, CanaryOperation.Modification);

        first.ShouldNotBeNull().FirstSource.ShouldBe(SignalSource.Etw);
        second.ShouldBeNull();
        first.SeenByWatcher.ShouldBeTrue();
    }

    [Fact]
    public void Repeated_signals_of_one_write_stay_one_detection_and_ETW_writes_are_all_kept()
    {
        _correlator.OnWatcher(Canary, CanaryOperation.Modification).ShouldNotBeNull();
        _correlator.OnWatcher(Canary, CanaryOperation.Modification).ShouldBeNull(); // the watcher fires twice
        _correlator.OnEtw(Write(Canary, pid: 4)).ShouldBeNull();
        CanaryDetection? none = _correlator.OnEtw(Write(Canary, pid: 4242));

        none.ShouldBeNull();
    }

    [Fact]
    public void After_the_2_s_window_the_same_operation_is_a_new_detection()
    {
        _correlator.OnWatcher(Canary, CanaryOperation.Modification).ShouldNotBeNull();
        _time.Advance(TimeSpan.FromMilliseconds(1999));
        _correlator.OnEtw(Write(Canary)).ShouldBeNull();
        _time.Advance(TimeSpan.FromMilliseconds(1));

        _correlator.OnEtw(Write(Canary)).ShouldNotBeNull();
    }

    [Fact]
    public void Another_operation_on_the_same_canary_is_another_detection()
    {
        _correlator.OnWatcher(Canary, CanaryOperation.Modification).ShouldNotBeNull();

        _correlator.OnEtw(new FileIoRecord(FileIoOperation.SetInfo, Canary, 4242, DateTime.UtcNow, 0, 13))
            .ShouldNotBeNull().Operation.ShouldBe(CanaryOperation.Deletion);
    }

    [Fact]
    public void The_key_ignores_the_case_of_the_path()
    {
        _correlator.OnWatcher(Canary, CanaryOperation.Rename).ShouldNotBeNull();

        _correlator.OnEtw(new FileIoRecord(FileIoOperation.Rename, Canary.ToUpperInvariant(), 4242, DateTime.UtcNow, 0, 10))
            .ShouldBeNull();
    }

    [Fact]
    public void A_write_only_ETW_sees_is_a_detection_and_not_a_miss()
    {
        _correlator.EtwRunning = true;

        _correlator.OnEtw(Write(Canary, pid: 4)).ShouldNotBeNull(); // a mapped page, PID 4: still a detection
        _time.Advance(TimeSpan.FromSeconds(3));
        _correlator.Sweep();

        _correlator.EtwMisses.ShouldBe(0);
    }

    [Fact]
    public void A_window_the_watcher_closes_alone_while_ETW_runs_is_an_ETW_miss()
    {
        _correlator.EtwRunning = true;

        _correlator.OnWatcher(Canary, CanaryOperation.Modification);
        _time.Advance(TimeSpan.FromSeconds(2));
        _correlator.Sweep();

        _correlator.EtwMisses.ShouldBe(1);
    }

    [Fact]
    public void Without_ETW_a_watcher_only_window_is_no_miss()
    {
        _correlator.EtwRunning = false;

        _correlator.OnWatcher(Canary, CanaryOperation.Modification);
        _time.Advance(TimeSpan.FromSeconds(2));
        _correlator.Sweep();

        _correlator.EtwMisses.ShouldBe(0);
    }

    [Theory]
    [InlineData(FileIoOperation.Write, 0u, 0, CanaryOperation.Modification)]
    [InlineData(FileIoOperation.Create, 5u, 0, CanaryOperation.Modification)]
    [InlineData(FileIoOperation.SetInfo, 0u, 20, CanaryOperation.Modification)]
    [InlineData(FileIoOperation.Rename, 0u, 10, CanaryOperation.Rename)]
    [InlineData(FileIoOperation.SetInfo, 0u, 65, CanaryOperation.Rename)]
    [InlineData(FileIoOperation.Delete, 0u, 13, CanaryOperation.Deletion)]
    [InlineData(FileIoOperation.SetInfo, 0u, 64, CanaryOperation.Deletion)]
    public void ETW_writes_map_to_the_watcher_classes(FileIoOperation operation, uint disposition, int infoClass, CanaryOperation expected)
    {
        CanaryCorrelator.Classify(new FileIoRecord(operation, Canary, 4242, DateTime.UtcNow, disposition, infoClass))
            .ShouldBe(expected);
    }

    private static FileIoRecord Write(string path, int pid = 4242) =>
        new(FileIoOperation.Write, path, pid, DateTime.UtcNow, 0, 0);
}
