using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Detection.ThreatIntel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;
using Xunit.Abstractions;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// AGT-AUD-002 — an audit wait never loses a detection or an action. Recording is one INSERT
/// into the waiting table; one writer per chain links the entries in the background, by
/// batches; a crash between the two loses nothing. On a real SQLCipher file, as the agent.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(AuditLoadCollection.Name)]
public sealed class AuditPipelineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"rg_audit_pipeline_{Guid.NewGuid():N}")).FullName;
    private readonly string _connectionString;
    private readonly AuditLogSigner _signer;
    private readonly List<ServiceProvider> _providers = [];

    public AuditPipelineTests(ITestOutputHelper output)
    {
        _output = output;
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _connectionString = $"Data Source={Path.Combine(_dir, "agent.db")};Password={key}";
        using (var ctx = NewContext())
        {
            ctx.Database.Migrate();
        }

        _signer = new AuditLogSigner(Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName,
            NullLogger<AuditLogSigner>.Instance);
    }

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private AgentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(_connectionString).Options);

    /// <summary>The agent's audit wiring, as in ServiceRegistration: one "process".</summary>
    private ServiceProvider Agent(IAuditChainWriter? writerOverride = null, AuditHealth? health = null,
        ILogger<AuditLogRepository>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite(_connectionString));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(health ?? new AuditHealth());
        services.AddSingleton<AuditChainWriter>();
        services.AddSingleton<IAuditChainWriter>(sp => writerOverride ?? sp.GetRequiredService<AuditChainWriter>());
        services.AddScoped<IAuditLogRepository>(sp => new AuditLogRepository(
            sp.GetRequiredService<AgentDbContext>(), _signer, sp.GetRequiredService<IAuditChainWriter>(),
            sp.GetRequiredService<AuditHealth>(), logger ?? NullLogger<AuditLogRepository>.Instance));
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static async Task<double> RecordAsync(ServiceProvider agent, string action, string details)
    {
        await using AsyncServiceScope scope = agent.CreateAsyncScope();
        long start = Stopwatch.GetTimestamp();
        await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().AppendAsync(action, details);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void Saturate(int entries)
    {
        using AgentDbContext db = NewContext();
        DateTime now = DateTime.UtcNow;
        db.PendingAuditEntries.AddRange(Enumerable.Range(0, entries).Select(i => new PendingAuditEntry
        {
            Action = "Burst", Details = $"{{\"n\":{i}}}", CreatedAt = now,
        }));
        db.SaveChanges();
    }

    private (int Waiting, List<AuditLog> Chain) State()
    {
        using AgentDbContext db = NewContext();
        return (db.PendingAuditEntries.Count(), db.AuditLogs.OrderBy(a => a.Sequence).ToList());
    }

    private async Task<bool> ChainIntactAsync()
    {
        await using AgentDbContext db = NewContext();
        return await new AuditLogRepository(db, _signer).VerifyChainIntegrityAsync();
    }

    private static async Task DrainAsync(AuditChainWriter writer) => await writer.ChainAllAsync(CancellationToken.None);

    // ── Recording does not wait for the chain ──────────────────────────────────

    [Fact(Timeout = 120_000)]
    public async Task Recording_does_not_wait_for_3000_entries_being_chained()
    {
        ServiceProvider agent = Agent();
        var writer = agent.GetRequiredService<AuditChainWriter>();
        Saturate(3000);
        await writer.StartAsync(CancellationToken.None);

        var latencies = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            latencies.Add(await RecordAsync(agent, "Probe", $"{{\"i\":{i}}}"));
        }

        await writer.StopAsync(CancellationToken.None);
        latencies.Sort();
        _output.WriteLine($"record while 3000 are chained: p50={latencies[24]:F1} ms max={latencies[^1]:F1} ms");
        latencies[^1].ShouldBeLessThan(2_000); // was: the whole queue (2.3 s p50, 4.6 s max at 1 000)

        (int waiting, List<AuditLog> chain) = State();
        waiting.ShouldBe(0);
        chain.Count.ShouldBe(3050);
        (await ChainIntactAsync()).ShouldBeTrue();
    }

    [Fact(Timeout = 120_000)]
    public async Task Nothing_is_dropped_and_the_chain_keeps_the_insertion_order()
    {
        ServiceProvider agent = Agent();
        var writer = agent.GetRequiredService<AuditChainWriter>();
        await writer.StartAsync(CancellationToken.None);

        // 1 000 concurrent recordings, then 100 in a known order.
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() => RecordAsync(agent, "Concurrent", $"{{\"i\":{i}}}"))));
        for (int i = 0; i < 100; i++)
        {
            await RecordAsync(agent, "Ordered", $"{{\"i\":{i}}}");
        }

        await writer.StopAsync(CancellationToken.None);
        (int waiting, List<AuditLog> chain) = State();
        waiting.ShouldBe(0);
        chain.Count.ShouldBe(1100);
        chain.Select(a => a.Sequence).ShouldBe(Enumerable.Range(1, 1100).Select(i => (long)i));
        chain.Where(a => a.Action == "Ordered").Select(a => a.Details)
            .ShouldBe(Enumerable.Range(0, 100).Select(i => $"{{\"i\":{i}}}"));
        (await ChainIntactAsync()).ShouldBeTrue();
    }

    // ── A crash loses nothing ──────────────────────────────────────────────────

    [Fact(Timeout = 120_000)]
    public async Task Entries_recorded_before_a_crash_are_chained_first_at_the_next_start()
    {
        // First "process": records, but dies before its writer ever runs.
        ServiceProvider first = Agent(writerOverride: new NeverChains());
        for (int i = 0; i < 300; i++)
        {
            await RecordAsync(first, "BeforeCrash", $"{{\"i\":{i}}}");
        }

        first.Dispose();
        SqliteConnection.ClearAllPools(); // nothing of the dead process survives but the file
        State().Waiting.ShouldBe(300);

        // Next start: Program chains what is waiting before the services run.
        ServiceProvider second = Agent();
        int chained = await second.GetRequiredService<AuditChainWriter>().ChainAllAsync(CancellationToken.None);
        await RecordAsync(second, "AfterRestart", "{}");
        await DrainAsync(second.GetRequiredService<AuditChainWriter>());

        chained.ShouldBe(300);
        (int waiting, List<AuditLog> chain) = State();
        waiting.ShouldBe(0);
        chain.Take(300).Select(a => a.Details).ShouldBe(Enumerable.Range(0, 300).Select(i => $"{{\"i\":{i}}}"));
        chain[^1].Action.ShouldBe("AfterRestart");
        (await ChainIntactAsync()).ShouldBeTrue();
    }

    [Fact(Timeout = 120_000)]
    public async Task A_crash_in_the_middle_of_a_chaining_transaction_keeps_every_entry_waiting()
    {
        ServiceProvider agent = Agent(writerOverride: new NeverChains());
        for (int i = 0; i < 50; i++)
        {
            await RecordAsync(agent, "Waiting", $"{{\"i\":{i}}}");
        }

        // A writer dies mid-transaction: links written, waiting rows deleted, never committed.
        using (AgentDbContext db = NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO AuditLogs (Id, Sequence, Action, Details, CurrentHash, CreatedAt, UpdatedAt) " +
                "VALUES ('00000000-0000-0000-0000-000000000001', 1, 'Half', 'none', 'x', '2026-10-15', '2026-10-15')");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM PendingAuditEntries");
            // no commit: the process is gone
        }

        SqliteConnection.ClearAllPools();
        State().Waiting.ShouldBe(50);
        State().Chain.ShouldBeEmpty();

        ServiceProvider restart = Agent();
        (await restart.GetRequiredService<AuditChainWriter>().ChainAllAsync(CancellationToken.None)).ShouldBe(50);
        State().Chain.Count.ShouldBe(50);
        (await ChainIntactAsync()).ShouldBeTrue();
    }

    // ── An entry that cannot be recorded never stops the caller ────────────────

    [Fact(Timeout = 60_000)]
    public async Task An_entry_that_cannot_be_recorded_is_counted_and_logged_critical_never_thrown()
    {
        var health = new AuditHealth();
        var log = new RecordingLogger<AuditLogRepository>();
        ServiceProvider agent = Agent(health: health, logger: log);
        using (AgentDbContext db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE PendingAuditEntries"); // as unwritable as a full disk
        }

        await Should.NotThrowAsync(() => RecordAsync(agent, "Lost", "{}"));

        health.Failures.ShouldBe(1);
        log.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains("Lost"));
    }

    // ── EXFIL: the timeout covers the action, the audit never triggers a fallback ──

    [Fact(Timeout = 180_000)]
    public async Task A_critical_exfil_finding_acts_at_once_while_3000_entries_wait_and_records_intent_then_result()
    {
        ServiceProvider agent = Agent();
        var writer = agent.GetRequiredService<AuditChainWriter>();
        Saturate(3000);
        await writer.StartAsync(CancellationToken.None);

        int throttles = 0, blocks = 0;
        var throttler = new Mock<IProcessThrottler>();
        throttler.Setup(t => t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>()))
            .Callback(() => Interlocked.Increment(ref throttles)).Returns(true);
        var firewall = new Mock<IFirewallManager>();
        firewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => Interlocked.Increment(ref blocks)).Returns(true);

        await using AsyncServiceScope scope = agent.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var engine = new ExfilActionEngine(
            new AlertOnlyAction(audit, NullLogger<AlertOnlyAction>.Instance),
            new ThrottleProcessAction(throttler.Object, audit, NullLogger<ThrottleProcessAction>.Instance),
            new BlockIpAction(firewall.Object, audit, NullLogger<BlockIpAction>.Instance),
            new ThreatIntelDataLoader(NullLogger<ThreatIntelDataLoader>.Instance),
            Mock.Of<IDataVolumeTracker>(), audit, new ExfilWatchOptions(), NullLogger<ExfilActionEngine>.Instance);

        long start = Stopwatch.GetTimestamp();
        await engine.ExecuteAsync(new ExfilFinding
        {
            RuleName = "LolbasExfil", MitreId = "T1218", Severity = ExfilSeverity.Critical, ProcessId = 9999,
            ProcessName = "certutil.exe", Destination = "203.0.113.7", BytesTransferred = 2_000_000, Description = "test",
        }, CancellationToken.None);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        await writer.StopAsync(CancellationToken.None);

        _output.WriteLine($"Critical EXFIL finding with 3000 entries waiting: {ms:F0} ms");
        ms.ShouldBeLessThan(2_000); // was 18.4 s, with the ExfilAlert entry lost
        (blocks, throttles).ShouldBe((1, 1)); // no parasitic fallback
        List<string> actions = State().Chain.Select(a => a.Action).Where(a => a.StartsWith("Exfil")).ToList();
        actions.ShouldNotContain("ExfilActionTimeout");
        actions.ShouldContain("ExfilAlert");
        actions.IndexOf("ExfilThrottleIntent").ShouldBeLessThan(actions.IndexOf("ExfilThrottle"));
        actions.IndexOf("ExfilBlockIntent").ShouldBeLessThan(actions.IndexOf("ExfilBlock"));
        (await ChainIntactAsync()).ShouldBeTrue();
    }

    // ── One writer per chain, not per process ──────────────────────────────────

    [Fact]
    public void The_chain_gate_is_per_database_not_per_process()
    {
        string a = Path.Combine(_dir, "a.db"), b = Path.Combine(_dir, "b.db");
        using var a1 = Ctx($"Data Source={a}");
        using var a2 = Ctx($"Data Source={Path.Combine(_dir, ".", "a.db")}");
        using var b1 = Ctx($"Data Source={b}");
        using var m1 = Ctx("Data Source=:memory:");
        using var m2 = Ctx("Data Source=:memory:");

        AuditChainGate.For(a1).ShouldBeSameAs(AuditChainGate.For(a2)); // one chain, one writer
        AuditChainGate.For(a1).ShouldNotBeSameAs(AuditChainGate.For(b1)); // two chains never wait for each other
        AuditChainGate.For(m1).ShouldNotBeSameAs(AuditChainGate.For(m2)); // two private in-memory databases

        static AgentDbContext Ctx(string cs) => new(new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(cs).Options);
    }

    /// <summary>A process that dies before its writer ever runs.</summary>
    private sealed class NeverChains : IAuditChainWriter
    {
        public void RequestChaining()
        {
        }
    }
}
