using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Persistence;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

/// <summary>
/// The learning phase of EXFIL WATCH belongs to the endpoint, not to the process (AGT-EXF-001).
/// Until 2026-10-22 its start was <c>DateTime.UtcNow</c> in the tracker's constructor, so every
/// restart of the agent began the seven days again: a workstation switched off each night never
/// left the phase, and the volume anomaly it is there to raise never fired. The start is stored,
/// written once, and read back as it is.
/// </summary>
public sealed class ExfilLearningPhaseStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AgentDbContext> _options;

    public ExfilLearningPhaseStoreTests()
    {
        // One in-memory database held open for the test: every context below is a new "process".
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(_connection).Options;
        using var db = new AgentDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ExfilLearningPhaseStore Store(AgentDbContext db, TimeProvider time) =>
        new(db, NullLogger<ExfilLearningPhaseStore>.Instance, time);

    [Fact]
    public void The_first_run_begins_the_phase_and_writes_it_once()
    {
        var clock = new FakeTime(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));
        using var db = new AgentDbContext(_options);

        DateTime started = Store(db, clock).StartedAtOrBegin(7);

        started.ShouldBe(clock.Now);
        db.ExfilLearningPhases.Count().ShouldBe(1);
        db.ExfilLearningPhases.Single().LearningPhaseDays.ShouldBe(7);
    }

    [Fact]
    public void A_restart_reads_the_stored_start_and_does_not_begin_the_phase_again()
    {
        var firstBoot = new FakeTime(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));
        DateTime started;
        using (var db = new AgentDbContext(_options))
        {
            started = Store(db, firstBoot).StartedAtOrBegin(7);
        }

        // The agent restarts five days later, as a hospital workstation does every night.
        var laterBoot = new FakeTime(firstBoot.Now.AddDays(5));
        DateTime afterRestart;
        using (var db = new AgentDbContext(_options))
        {
            afterRestart = Store(db, laterBoot).StartedAtOrBegin(7);
        }

        afterRestart.ShouldBe(started);
        using var check = new AgentDbContext(_options);
        check.ExfilLearningPhases.Count().ShouldBe(1); // written once, never moved
    }

    [Fact]
    public void A_tracker_built_from_the_stored_start_leaves_the_phase_on_time_despite_restarts()
    {
        var firstBoot = new FakeTime(DateTime.UtcNow.AddDays(-8)); // learning began 8 days ago
        using (var db = new AgentDbContext(_options))
        {
            Store(db, firstBoot).StartedAtOrBegin(7);
        }

        DateTime stored;
        using (var db = new AgentDbContext(_options))
        {
            stored = Store(db, new FakeTime(DateTime.UtcNow)).StartedAtOrBegin(7);
        }

        var tracker = new DataVolumeTracker(NullLogger<DataVolumeTracker>.Instance, learningPhaseDays: 7, startedAt: stored);

        tracker.LearningStartedAt.ShouldBe(stored);
        tracker.IsLearningPhase.ShouldBeFalse(); // 8 days > 7: the phase is over, restarts or not
    }

    [Fact]
    public void Without_a_stored_start_the_tracker_still_learns_from_now_as_before()
    {
        var tracker = new DataVolumeTracker(NullLogger<DataVolumeTracker>.Instance, learningPhaseDays: 7);

        tracker.IsLearningPhase.ShouldBeTrue();
    }

    private sealed class FakeTime(DateTime now) : TimeProvider
    {
        public DateTime Now { get; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
