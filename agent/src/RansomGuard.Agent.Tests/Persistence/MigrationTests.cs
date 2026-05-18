using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Tests that EF Core migrations apply cleanly to a fresh SQLite database.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _dbPath;

    public MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ransomguard_migration_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void Migrations_should_apply_cleanly_to_fresh_database()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.DetectionEvents.Count().ShouldBe(0);
        context.Alerts.Count().ShouldBe(0);
        context.AgentStates.Count().ShouldBe(0);
        context.AuditLogs.Count().ShouldBe(0);
    }

    [Fact]
    public void Migrations_should_be_idempotent()
    {
        using (var context1 = CreateContext())
        {
            context1.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();

        using var context2 = CreateContext();
        context2.Database.Migrate();
        context2.DetectionEvents.Count().ShouldBe(0);
    }

    [Fact]
    public void Migrations_should_have_pending_migrations_before_apply()
    {
        using var context = CreateContext();

        IEnumerable<string> pending = context.Database.GetPendingMigrations();
        pending.ShouldNotBeEmpty();
        pending.ShouldContain(m => m.Contains("InitialCreate"));

        context.Database.Migrate();

        IEnumerable<string> afterMigrate = context.Database.GetPendingMigrations();
        afterMigrate.ShouldBeEmpty();
    }

    private AgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new AgentDbContext(options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }
}
