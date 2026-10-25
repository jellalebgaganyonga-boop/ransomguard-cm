using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// When a canary is marked as the agent's own write, relative to when it is written.
///
/// <para>
/// The mark used to be set by the deployment service, on the line after CreateCanaryAsync
/// returned -- in other words after the bytes were already on disk. The comment there said the
/// mark was set "before the monitor starts watching", and that was the mistake: the deployment
/// service and the monitor are both hosted services and start together, so by the time the first
/// canary is written the watcher is usually already watching. The FileSystemWatcher carries no
/// PID, so it turned the agent's own creation into a Critical "canary modified", and ETW could
/// not contradict it because ETW filters the agent's own PID out -- the alert arrived with no
/// process named at all.
/// </para>
///
/// <para>
/// A fresh install on the bench produced fifteen of those (PC-LAB, 2026-10-25), each within a
/// second of its canary being created, plus a SentinelIsolationRecommended on top because three
/// canaries had been "written by nobody" inside a minute. The ordering is the fix, so the
/// ordering is what these tests hold: the mark exists before the write, not after it.
/// </para>
/// </summary>
public sealed class CanaryOwnWriteOrderingTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _repository;
    private readonly string _testDir;

    public CanaryOwnWriteOrderingTests()
    {
        DbContextOptions<AgentDbContext> options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new SentinelCanaryRepository(_context);

        _testDir = Path.Combine(Path.GetTempPath(), $"rg_ownwrite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch (IOException) { }
    }

    private CanaryFileService Service(AgentOwnWrites own) =>
        new(_repository, new Mock<ILogger<CanaryFileService>>().Object, own);

    /// <summary>
    /// Watches the directory the way SENTINEL's monitor does and records, for each event, whether
    /// the path was already marked as the agent's own write at the moment the event arrived.
    /// </summary>
    private sealed class OwnWriteObserver : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly AgentOwnWrites _own;

        public OwnWriteObserver(string directory, AgentOwnWrites own)
        {
            _own = own;
            _watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = false };
            _watcher.Created += OnEvent;
            _watcher.Changed += OnEvent;
            _watcher.EnableRaisingEvents = true;
        }

        /// <summary>Events that arrived while the path was NOT marked: each one is an alert.</summary>
        public List<string> Unclaimed { get; } = [];

        public int Seen { get; private set; }

        private void OnEvent(object sender, FileSystemEventArgs e)
        {
            lock (Unclaimed)
            {
                Seen++;
                // Exactly the monitor's question, asked at exactly the monitor's moment.
                if (!_own.IsOwn(e.FullPath))
                {
                    Unclaimed.Add(e.FullPath);
                }
            }
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnEvent;
            _watcher.Changed -= OnEvent;
            _watcher.Dispose();
        }
    }

    [Fact]
    public async Task A_canary_is_the_agents_own_write_from_the_moment_it_is_created()
    {
        var own = new AgentOwnWrites();
        using var observer = new OwnWriteObserver(_testDir, own);

        SentinelCanary canary = await Service(own).CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // The watcher is asynchronous; give its events time to arrive.
        for (int i = 0; i < 50 && observer.Seen == 0; i++)
        {
            await Task.Delay(20);
        }

        await Task.Delay(100);

        lock (observer.Unclaimed)
        {
            observer.Unclaimed.ShouldBeEmpty(
                "a canary the agent has just planted was seen by the watcher before it was marked, "
                + "which is one Critical 'canary modified' per canary on a fresh install");
        }

        own.IsOwn(canary.FilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task Every_canary_of_a_fresh_install_is_claimed_not_just_the_first()
    {
        // Six on the first bench run, fifteen on the second: the count follows the number of
        // canaries, so the test plants several rather than one.
        var own = new AgentOwnWrites();
        using var observer = new OwnWriteObserver(_testDir, own);

        string[] templates = ["dossier_patient", "analyses_laboratoire", "imagerie_medicale"];
        foreach (string template in templates)
        {
            await Service(own).CreateCanaryAsync(_testDir, template, "0001_");
        }

        for (int i = 0; i < 50 && observer.Seen < templates.Length; i++)
        {
            await Task.Delay(20);
        }

        await Task.Delay(100);

        lock (observer.Unclaimed)
        {
            observer.Unclaimed.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Restoring_a_canary_is_the_agents_own_write_too()
    {
        var own = new AgentOwnWrites();
        SentinelCanary canary = await Service(own).CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Let the creation's grace lapse so the restore has to stand on its own mark.
        own.Clear(canary.FilePath);
        own.IsOwn(canary.FilePath).ShouldBeFalse();

        using var observer = new OwnWriteObserver(_testDir, own);
        await Service(own).RestoreCanaryAsync(canary);

        for (int i = 0; i < 50 && observer.Seen == 0; i++)
        {
            await Task.Delay(20);
        }

        await Task.Delay(100);

        lock (observer.Unclaimed)
        {
            observer.Unclaimed.ShouldBeEmpty(
                "repairing a decoy raised a second alert on the file it had just repaired");
        }
    }

    [Fact]
    public async Task A_canary_the_agent_planted_still_verifies_against_its_stored_hash()
    {
        // The alerts on the bench carried no details and no process, so the integrity check was a
        // candidate cause before the ordering was found. It is not: a planted canary matches the
        // hash recorded for it, and this says so rather than leaving it assumed.
        var own = new AgentOwnWrites();
        CanaryFileService service = Service(own);

        SentinelCanary canary = await service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        (await service.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_write_by_something_else_is_still_reported()
    {
        // The guard must not become a way to write to a canary unnoticed.
        var own = new AgentOwnWrites();
        SentinelCanary canary = await Service(own).CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        own.Clear(canary.FilePath);

        using var observer = new OwnWriteObserver(_testDir, own);

        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.WriteAllTextAsync(canary.FilePath, "encrypted by something that is not the agent");

        for (int i = 0; i < 50 && observer.Seen == 0; i++)
        {
            await Task.Delay(20);
        }

        lock (observer.Unclaimed)
        {
            observer.Unclaimed.ShouldNotBeEmpty("a write by anything other than the agent must raise an alert");
        }
    }
}
