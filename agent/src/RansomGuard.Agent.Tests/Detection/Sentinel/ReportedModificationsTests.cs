using RansomGuard.Agent.Core.Detection.Sentinel;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel;

/// <summary>
/// The set that keeps the periodic integrity sweep from reporting a modification the real-time
/// path has already reported and is restoring.
/// </summary>
public sealed class ReportedModificationsTests
{
    private const string Path = @"C:\Users\x\Desktop\0001_patient_record_abcd1234.txt";

    private readonly MovableTime _time = new(new DateTime(2026, 10, 26, 0, 26, 38, DateTimeKind.Utc));
    private readonly ReportedModifications _reported;

    public ReportedModificationsTests() => _reported = new ReportedModifications(_time);

    [Fact]
    public void A_path_nobody_reported_is_not_being_handled()
    {
        _reported.IsBeingHandled(Path).ShouldBeFalse();
    }

    [Fact]
    public void A_reported_modification_is_being_handled()
    {
        _reported.Reported(Path);

        _reported.IsBeingHandled(Path).ShouldBeTrue();
    }

    [Fact]
    public void Once_handled_the_sweep_speaks_for_the_path_again()
    {
        _reported.Reported(Path);
        _reported.Handled(Path);

        // This is the half that matters: the restore has finished, so the next write on this decoy
        // is a new attack and the sweep must be free to report it. Nothing is forgiven for a
        // fixed stretch of time.
        _reported.IsBeingHandled(Path).ShouldBeFalse();
        _reported.Count.ShouldBe(0);
    }

    [Fact]
    public void An_entry_nobody_ever_cleared_stops_silencing_its_decoy()
    {
        _reported.Reported(Path);
        _time.Advance(ReportedModifications.MaxHandlingTime);

        _reported.IsBeingHandled(Path).ShouldBeFalse("a lost entry must not silence a decoy for good");
        _reported.Count.ShouldBe(0, "and it is dropped as it is read, so the set cannot grow");
    }

    [Fact]
    public void The_path_is_matched_whatever_its_case()
    {
        _reported.Reported(Path);

        _reported.IsBeingHandled(Path.ToUpperInvariant()).ShouldBeTrue();
    }

    [Fact]
    public void One_decoy_being_handled_does_not_silence_another()
    {
        _reported.Reported(Path);

        _reported.IsBeingHandled(@"C:\Users\x\Desktop\0001_lab_results_99998888.txt").ShouldBeFalse();
    }

    /// <summary>A clock the test moves by hand, so the expiry is tested without waiting a minute.</summary>
    private sealed class MovableTime(DateTime start) : TimeProvider
    {
        private DateTime _now = start;

        public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
