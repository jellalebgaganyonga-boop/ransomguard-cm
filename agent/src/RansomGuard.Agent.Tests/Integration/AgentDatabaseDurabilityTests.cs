using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Service;
using Serilog;
using Shouldly;
using Xunit.Abstractions;

namespace RansomGuard.Agent.Tests.Integration;

/// <summary>
/// The durability of the waiting audit table (AGT-AUD-002) after a power cut, not only after a
/// killed process, rests on the SQLite settings of the agent's own connection. Read on a
/// database opened exactly as the agent opens it: ServiceRegistration, SQLCipher key, migrations.
/// </summary>
public sealed class AgentDatabaseDurabilityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"rg_durability_{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Pragma(AgentDbContext db, string name)
    {
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private ServiceProvider Agent(string connectionString)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "RansomGuard.Agent.Service", "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Server:BaseUrl"] = "https://localhost",
                ["Agent:Database:ConnectionString"] = connectionString,
                // This test's own keys, not the machine-wide default shared with parallel tests.
                ["Agent:Database:KeyDirectory"] = Path.Combine(_dir, "keys"),
                ["Agent:Logging:LogFilePath"] = Path.Combine(_dir, "agent.log"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerilog(new LoggerConfiguration().CreateLogger());
        ServiceRegistration.ConfigureServices(services, config);
        return services.BuildServiceProvider();
    }

    private (string JournalMode, string Synchronous) OpenedByTheAgent(ServiceProvider provider)
    {
        // As Program.EnsureDatabase, then a later connection, as every repository opens it.
        using (IServiceScope scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.Migrate();
        }

        using IServiceScope reading = provider.CreateScope();
        var db = reading.ServiceProvider.GetRequiredService<AgentDbContext>();
        (string, string) pragmas = (Pragma(db, "journal_mode"), Pragma(db, "synchronous"));
        output.WriteLine($"AGENT DATABASE PRAGMA journal_mode={pragmas.Item1} synchronous={pragmas.Item2} (0=OFF 1=NORMAL 2=FULL 3=EXTRA)");
        return pragmas;
    }

    [Fact]
    public void The_agent_connection_runs_in_WAL_with_synchronous_full()
    {
        using ServiceProvider agent = Agent($"Data Source={Path.Combine(_dir, "agent.db")}");

        (string journalMode, string synchronous) = OpenedByTheAgent(agent);

        // FULL: a committed INSERT into PendingAuditEntries is on disk before AppendAsync returns.
        // In WAL mode, NORMAL could lose the last commits on a power cut.
        journalMode.ShouldBe("wal");
        synchronous.ShouldBe("2");
    }

    [Fact]
    public void A_database_created_without_EF_in_rollback_mode_is_put_in_WAL_by_the_agent()
    {
        // An existing file EF did not create (an older install, a restore): EF Core only switches
        // to WAL when it creates the file, so only the agent's explicit setting can do it.
        string key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string connectionString = $"Data Source={Path.Combine(_dir, "existing.db")};Password={key}";
        using (var existing = new SqliteConnection(connectionString))
        {
            existing.Open();
            using SqliteCommand command = existing.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE; CREATE TABLE Legacy (Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA journal_mode;";
            Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture).ShouldBe("delete");
        }

        SqliteConnection.ClearAllPools();
        using ServiceProvider agent = Agent(connectionString);

        (string journalMode, string synchronous) = OpenedByTheAgent(agent);

        journalMode.ShouldBe("wal");
        synchronous.ShouldBe("2");
    }
}
