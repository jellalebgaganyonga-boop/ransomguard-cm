using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// The audit chain under concurrent writers, and the upgrade of a database created before
/// <c>AuditLog.Sequence</c> existed. A real SQLite file migrated like production, one
/// DbContext per writer as in production scopes.
/// </summary>
public sealed class AuditLogConcurrencyAndUpgradeTests : IDisposable
{
    private const string MigrationBeforeSequence = "20260702080235_AddPendingAlertUpload";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rg-audit-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AgentDbContext> _options;

    public AuditLogConcurrencyAndUpgradeTests()
    {
        _options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Fifty_parallel_appends_keep_one_unforked_chain()
    {
        await using (var ctx = new AgentDbContext(_options))
        {
            await ctx.Database.MigrateAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            await using var ctx = new AgentDbContext(_options);
            await new AuditLogRepository(ctx).AppendAsync("ConcurrentWrite", $"entry {i}");
        })));

        await using var verifyCtx = new AgentDbContext(_options);
        List<AuditLog> rows = await verifyCtx.AuditLogs.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync();

        rows.Count.ShouldBe(50);
        rows.Select(r => r.Sequence).ShouldBe(Enumerable.Range(1, 50).Select(i => (long)i));
        rows.GroupBy(r => r.PreviousHash ?? "GENESIS").ShouldAllBe(g => g.Count() == 1); // no fork
        (await new AuditLogRepository(verifyCtx).VerifyChainIntegrityAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Database_created_before_the_sequence_upgrades_with_its_chain_intact()
    {
        // A database at the previous schema, holding a 20-entry chain written the old way.
        await using (var ctx = new AgentDbContext(_options))
        {
            await ctx.GetService<IMigrator>().MigrateAsync(MigrationBeforeSequence);

            string? previous = null;
            var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 20; i++)
            {
                Guid id = Guid.NewGuid();
                string details = $"legacy {i}";
                DateTime ts = t0.AddSeconds(i);
                string hash = AuditLog.ComputeHash("Legacy", details, ts, previous);
                await ctx.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO AuditLogs (Id, Action, Details, PreviousHash, CurrentHash, CreatedAt, UpdatedAt) VALUES ({id}, 'Legacy', {details}, {previous}, {hash}, {ts}, {ts})");
                previous = hash;
            }

            // Upgrade to the current schema.
            await ctx.Database.MigrateAsync();
        }

        await using var upgraded = new AgentDbContext(_options);
        var repository = new AuditLogRepository(upgraded);

        List<long> sequences = await upgraded.AuditLogs.AsNoTracking()
            .OrderBy(a => a.Sequence).Select(a => a.Sequence).ToListAsync();
        sequences.ShouldBe(Enumerable.Range(1, 20).Select(i => (long)i));
        (await repository.VerifyChainIntegrityAsync()).ShouldBeTrue();

        await repository.AppendAsync("AfterUpgrade", "first entry written by the new code");

        AuditLog? latest = await repository.GetLatestAsync();
        latest.ShouldNotBeNull();
        latest.Sequence.ShouldBe(21);
        (await repository.VerifyChainIntegrityAsync()).ShouldBeTrue();
    }
}
