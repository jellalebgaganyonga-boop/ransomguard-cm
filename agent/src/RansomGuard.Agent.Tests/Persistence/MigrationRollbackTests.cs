using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Verifies Sprint 3 migrations can be applied and rolled back cleanly.
/// Uses a real SQLite file (not in-memory) to test actual migration SQL.
/// </summary>
public sealed class MigrationRollbackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public MigrationRollbackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rg_migration_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public void AllMigrations_ApplyCleanly()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        // Verify all tables exist by querying them
        context.DetectionEvents.Count().ShouldBe(0);
        context.Alerts.Count().ShouldBe(0);
        context.AuditLogs.Count().ShouldBe(0);
        context.Set<SentinelCanary>().Count().ShouldBe(0);
        context.Set<CanaryAlert>().Count().ShouldBe(0);
        context.EntropyBaselines.Count().ShouldBe(0);
        context.EntropyAlerts.Count().ShouldBe(0);
        context.Set<GenealogyRecord>().Count().ShouldBe(0);
    }

    [Fact]
    public async Task Sprint3Data_PersistsAfterMigration()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        // Insert Sprint 3 data
        context.EntropyBaselines.Add(new EntropyBaseline
        {
            FilePath = @"C:\test\file.txt",
            DirectoryPath = @"C:\test",
            FileExtension = ".txt",
            EntropyValue = 4.5,
            FileSize = 1024
        });

        context.EntropyAlerts.Add(new EntropyAlert
        {
            FilePath = @"C:\test\file.txt",
            RuleId = 1,
            RuleName = "AbsoluteHighEntropy",
            Severity = "Critical",
            BaselineEntropy = 4.5,
            CurrentEntropy = 7.9,
            Delta = 3.4
        });

        context.Set<GenealogyRecord>().Add(new GenealogyRecord
        {
            AlertId = Guid.NewGuid(),
            ProcessTreeJson = "{}",
            SuspiciousPatternsJson = "[]",
            RootProcessId = 1234,
            RootProcessName = "test",
            Summary = "test -> cmd"
        });

        await context.SaveChangesAsync();

        // Verify data persisted
        (await context.EntropyBaselines.CountAsync()).ShouldBe(1);
        (await context.EntropyAlerts.CountAsync()).ShouldBe(1);
        (await context.Set<GenealogyRecord>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public void Sprint3Migrations_RollbackViaDownMethods()
    {
        // Apply all
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        // Rollback Sprint 3 migrations (AddGenealogyRecord and AddEntropyEntities)
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();

            // Rollback to AddAuditLogSignature (pre-Sprint 3)
            migrator.Migrate("20260518053238_AddAuditLogSignature");
        }

        // Verify Sprint 3 tables are gone
        using (var context = CreateContext())
        {
            // EntropyBaselines table should not exist
            Should.Throw<Exception>(() => context.EntropyBaselines.Count());
        }

        // Re-apply all
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        // Verify Sprint 3 tables are back
        using (var context = CreateContext())
        {
            context.EntropyBaselines.Count().ShouldBe(0);
            context.EntropyAlerts.Count().ShouldBe(0);
            context.Set<GenealogyRecord>().Count().ShouldBe(0);
        }
    }

    [Fact]
    public void MigrationsHaveAllFiveExpected()
    {
        using var context = CreateContext();

        var migrations = context.Database.GetMigrations().ToList();

        migrations.Count.ShouldBeGreaterThanOrEqualTo(5);
        migrations.ShouldContain(m => m.Contains("InitialCreate"));
        migrations.ShouldContain(m => m.Contains("AddSentinelEntities"));
        migrations.ShouldContain(m => m.Contains("AddAuditLogSignature"));
        migrations.ShouldContain(m => m.Contains("AddEntropyEntities"));
        migrations.ShouldContain(m => m.Contains("AddGenealogyRecord"));
    }

    private AgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new AgentDbContext(options);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
