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

    [Fact]
    public void The_agent_connection_commits_durably_synchronous_full()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "RansomGuard.Agent.Service", "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Server:BaseUrl"] = "https://localhost",
                ["Agent:Database:ConnectionString"] = $"Data Source={Path.Combine(_dir, "agent.db")}",
                ["Agent:Logging:LogFilePath"] = Path.Combine(_dir, "agent.log"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerilog(new LoggerConfiguration().CreateLogger());
        ServiceRegistration.ConfigureServices(services, config);
        using ServiceProvider provider = services.BuildServiceProvider();

        // As Program.EnsureDatabase: the agent creates the file through its migrations.
        using (IServiceScope scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.Migrate();
        }

        // A later connection of the agent, as every repository opens it.
        using IServiceScope reading = provider.CreateScope();
        var db = reading.ServiceProvider.GetRequiredService<AgentDbContext>();
        string journalMode = Pragma(db, "journal_mode");
        string synchronous = Pragma(db, "synchronous");
        output.WriteLine($"AGENT DATABASE PRAGMA journal_mode={journalMode} synchronous={synchronous} (0=OFF 1=NORMAL 2=FULL 3=EXTRA)");

        // FULL: a committed INSERT into PendingAuditEntries is on disk before AppendAsync returns,
        // in rollback-journal mode and in WAL mode alike. NORMAL in WAL mode could lose the last
        // commits on a power cut.
        synchronous.ShouldBe("2", $"journal_mode={journalMode}");
    }
}
