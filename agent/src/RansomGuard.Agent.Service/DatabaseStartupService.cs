using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Creates the database and applies its migrations, then lets the rest of the agent start.
///
/// <para>
/// This used to run in Program.cs before <c>host.Run()</c>, which meant Windows was not told the
/// service was running until the schema existed. On a first-ever install that is where the
/// database is created and Entity Framework is compiled for the first time: on PC-RECEPTION on
/// 2026-10-26 it took over two minutes, the Service Control Manager gave up at 30 s with 1053, and
/// the installation survived only because the installer tried a second time.
/// </para>
///
/// <para>
/// It is registered first, so it is the first <c>ExecuteAsync</c> to run; everything that reads the
/// database waits on <see cref="DatabaseReady"/> before touching it. The audit entries left
/// unchained by a previous stop are chained here too, still before any service records a new one
/// (AGT-AUD-002), because that ordering is what makes the chain verifiable.
/// </para>
/// </summary>
public sealed class DatabaseStartupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseReady _ready;
    private readonly AuditChainWriter _chainWriter;
    private readonly AuditHealth _health;
    private readonly ILogger<DatabaseStartupService> _logger;

    /// <summary>Creates the service.</summary>
    public DatabaseStartupService(
        IServiceScopeFactory scopeFactory,
        DatabaseReady ready,
        AuditChainWriter chainWriter,
        AuditHealth health,
        ILogger<DatabaseStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _ready = ready;
        _chainWriter = chainWriter;
        _health = health;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the startup path before anything else: this is the work that used to hold the
        // service start open, and the whole point of moving it here is that Windows no longer
        // waits for it.
        await Task.Yield();

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

            EnsureDirectory(context);

            await context.Database.MigrateAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Database migrations applied successfully");

        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return; // stopping; nothing is waiting on us any more
        }
        catch (Exception ex)
        {
            // Everything waiting fails with this, rather than waiting for ever. A detector that
            // never starts and never says why is worse than one that stops loudly.
            _logger.LogCritical(ex, "The database could not be prepared; the agent cannot protect this workstation");
            _ready.Fail(ex);
            return;
        }

        _ready.Signal();

        // Only now: the entries a previous stop left unchained, before any service records a new
        // one (AGT-AUD-002). A failure here does not stop the agent -- the entries stay waiting
        // and the chain writer retries -- so it must not fail the readiness signal above either.
        try
        {
            int chained = await _chainWriter.ChainAllAsync(stoppingToken).ConfigureAwait(false);
            if (chained > 0)
            {
                _logger.LogInformation(
                    "{Count} audit entries recorded before the last stop were chained first", chained);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping
        }
        catch (Exception ex)
        {
            _health.RecordFailure();
            _logger.LogCritical(ex,
                "Audit entries left waiting could not be chained at startup; the chain writer will retry");
        }
    }

    /// <summary>The folder the SQLite file lives in, which on a first install does not exist.</summary>
    private void EnsureDirectory(AgentDbContext context)
    {
        string? connectionString = context.Database.GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        const string dataSourcePrefix = "Data Source=";
        int index = connectionString.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        string path = connectionString[(index + dataSourcePrefix.Length)..].Trim();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created database directory: {Directory}", directory);
        }
    }
}
