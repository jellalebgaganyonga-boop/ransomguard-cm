using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Security;
using RansomGuard.Agent.Core.Security.AntiTampering;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Core.Security.RateLimiting;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Service;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Bootstrap logger for startup errors before host is built
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // CLI command: --verify-audit-log
    if (args.Contains("--verify-audit-log"))
    {
        await VerifyAuditLogAsync();
        return;
    }

    // CLI command: --baseline-reset
    if (args.Contains("--baseline-reset"))
    {
        await BaselineResetAsync();
        return;
    }

    // CLI command: --baseline-export
    if (args.Contains("--baseline-export"))
    {
        await BaselineExportAsync();
        return;
    }

    // CLI command: --check-threat-intel-updates
    if (args.Contains("--check-threat-intel-updates"))
    {
        CheckThreatIntelUpdates();
        return;
    }

    // CLI command: --apply-threat-intel-update
    if (args.Contains("--apply-threat-intel-update"))
    {
        await ApplyThreatIntelUpdateAsync();
        return;
    }

    Log.Information("RansomGuard-CM Agent starting up");

    var builder = Host.CreateApplicationBuilder(args);
    Log.Information("Host environment: {Environment}", builder.Environment.EnvironmentName);

    // Validate the configuration ONCE, before anything consumes it (logging, services,
    // database): everything below reads this validated object, never raw IConfiguration.
    AgentConfigurationLoadResult load = AgentConfigurationLoader.Load(
        builder.Configuration, builder.Environment.EnvironmentName);
    if (load.Configuration is null)
    {
        ReportStartupRefusal(load.Errors);
        Environment.ExitCode = 1;
        return;
    }
    AgentConfiguration agentConfig = load.Configuration;

    // TLS to the GRID: the loader refused TrustAnyCertificate outside Development/localhost.
    if (agentConfig.Server.TrustAnyCertificate)
    {
        Log.Warning(ServerTlsPolicy.ActiveWarning);
    }

    // Enable running as a Windows Service
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "RansomGuard-CM Agent";
    });

    // Configure Serilog from appsettings.json + programmatic config
    builder.Services.AddSerilog((services, loggerConfig) =>
    {
        // Validated object only: the path is absolute, sizes and retention are in range.
        string logPath = EnvironmentVariableResolver.ResolvePath(agentConfig.Logging.LogFilePath);
        int maxFileSizeMb = agentConfig.Logging.MaxFileSizeMB;
        int retainedFiles = agentConfig.Logging.RetainedFileCount;

        // The one notion of environment is the .NET host environment (DOTNET_ENVIRONMENT).
        // A Windows service started without it runs as Production, which is what we want.
        bool isProduction = builder.Environment.IsProduction();

        loggerConfig
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("RansomGuard", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName();

        if (!isProduction)
        {
            loggerConfig.WriteTo.Console();
        }

        // File sink: daily rotation, size limit, retained count
        if (isProduction)
        {
            loggerConfig.WriteTo.File(
                new CompactJsonFormatter(),
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: maxFileSizeMb * 1024L * 1024L,
                retainedFileCountLimit: retainedFiles,
                shared: true);
        }
        else
        {
            loggerConfig.WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: maxFileSizeMb * 1024L * 1024L,
                retainedFileCountLimit: retainedFiles,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        }

        // Windows Event Log sink (production only)
        if (isProduction && OperatingSystem.IsWindows())
        {
            loggerConfig.WriteTo.EventLog(
                source: "RansomGuard-CM",
                restrictedToMinimumLevel: LogEventLevel.Warning);
        }
    });

    // All agent services via shared registration (enables IHost-based testing)
    ServiceRegistration.ConfigureServices(builder.Services, builder.Configuration);

    // The detection monitors (hosted services), in start order
    ServiceRegistration.AddMonitors(builder.Services);

    var host = builder.Build();

    // Ensure database directory exists and apply migrations
    EnsureDatabase(host.Services);

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RansomGuard-CM Agent terminated unexpectedly");
    // Never exit 0 after a crash: a script reading the exit code would take it for success.
    if (Environment.ExitCode == 0)
    {
        Environment.ExitCode = 1;
    }
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Ensures the database directory exists and applies pending EF Core migrations.
/// Fails fast if migrations cannot be applied (critical startup error).
/// </summary>
static void EnsureDatabase(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

    string? connectionString = context.Database.GetConnectionString();
    if (connectionString is not null)
    {
        const string dataSourcePrefix = "Data Source=";
        int idx = connectionString.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            string dbPath = connectionString[(idx + dataSourcePrefix.Length)..].Trim();
            string? dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
                Log.Information("Created database directory: {Directory}", dbDir);
            }
        }
    }

    try
    {
        context.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to apply database migrations. The agent cannot start with a broken schema");
        throw;
    }
}

/// <summary>
/// Reports a startup refusal where it is always seen: the console, always; the Windows Event
/// Log, best effort only. Writing to the Event Log needs a registered source (elevation on
/// first use); if that fails, the failure must never hide the message that explains why the
/// agent refuses to start.
/// </summary>
static void ReportStartupRefusal(IReadOnlyList<string> errors)
{
    string message = "RansomGuard-CM Agent refuses to start: its configuration is invalid."
        + Environment.NewLine
        + string.Join(Environment.NewLine, errors.Select(e => "  - " + e));

    // The bootstrap logger writes to the console: always seen.
    Log.Fatal(message);

    if (OperatingSystem.IsWindows())
    {
        try
        {
            System.Diagnostics.EventLog.WriteEntry(
                "RansomGuard-CM", message, System.Diagnostics.EventLogEntryType.Error);
        }
        catch (Exception ex)
        {
            // Best effort: the console already carries the message; say why the Event Log did not.
            Console.Error.WriteLine($"(Windows Event Log not written: {ex.Message})");
        }
    }
}

/// <summary>
/// CLI: loads the configuration exactly as the service does (validated once), rooted at the
/// executable's directory -- never the caller's working directory. Exit code 2 when refused.
/// </summary>
static AgentConfiguration? LoadCliConfiguration()
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
    });

    AgentConfigurationLoadResult load = AgentConfigurationLoader.Load(
        builder.Configuration, builder.Environment.EnvironmentName);
    if (load.Configuration is null)
    {
        ReportStartupRefusal(load.Errors);
        Environment.ExitCode = CliExitCode.Refused;
        return null;
    }
    return load.Configuration;
}

/// <summary>
/// CLI: opens the INSTALLED encrypted database. Refuses (exit code 2, nothing done) when it
/// does not exist, instead of creating an empty one: a verifier must never create what it
/// verifies.
/// </summary>
static InstalledDatabase? OpenInstalledDatabase(AgentConfiguration config)
{
    string connectionString = EnvironmentVariableResolver.ResolvePath(config.Database.ConnectionString);
    string dbPath = Path.GetFullPath(ConfigurationValueRules.DataSource(connectionString) ?? string.Empty);
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"REFUSED: the agent database does not exist: {dbPath}");
        Console.Error.WriteLine("Nothing was done. Is the agent installed and has it started once?");
        Environment.ExitCode = CliExitCode.Refused;
        return null;
    }

    SQLitePCL.Batteries_V2.Init();
    string keyDir = EnvironmentVariableResolver.ResolvePath(config.Database.KeyDirectory);
    var dbKeyManager = new DatabaseKeyManager(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
    string dbKey = dbKeyManager.GetOrCreateKey();

    string encryptedConnectionString = connectionString.Contains("Password=")
        ? connectionString
        : $"{connectionString};Password={dbKey}";

    var options = new DbContextOptionsBuilder<AgentDbContext>()
        .UseSqlite(encryptedConnectionString)
        .Options;

    return new InstalledDatabase(new AgentDbContext(options), dbPath, keyDir);
}

/// <summary>
/// Verifies the audit log hash chain and Ed25519 signatures of the installed database.
/// Exit 0 if valid, 1 if the integrity check fails, 2 if refused (nothing was done).
/// </summary>
static async Task VerifyAuditLogAsync()
{
    Console.WriteLine("=== Audit Log Verification ===");

    AgentConfiguration? config = LoadCliConfiguration();
    if (config is null) return;
    InstalledDatabase? db = OpenInstalledDatabase(config);
    if (db is null) return;

    // No Migrate(): a verifier must not alter what it verifies. A schema older than this
    // verifier is refused instead of read wrongly (the installed agent upgrades it at start).
    using AgentDbContext context = db.Context;
    List<string> pending = context.Database.GetPendingMigrations().ToList();
    if (pending.Count > 0)
    {
        Console.Error.WriteLine($"REFUSED: the database schema of {db.Path} is older than this verifier.");
        Console.Error.WriteLine($"Pending migrations: {string.Join(", ", pending)}");
        Console.Error.WriteLine("Nothing was done. Start the agent once so it upgrades its database, then verify.");
        Environment.ExitCode = CliExitCode.Refused;
        return;
    }
    var signer = new AuditLogSigner(db.KeyDirectory,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
    var repo = new AuditLogRepository(context, signer);

    int entryCount = await context.AuditLogs.CountAsync();
    Console.WriteLine($"Database examined: {db.Path}");
    Console.WriteLine($"Audit log entries read: {entryCount}");

    if (entryCount == 0)
    {
        Console.WriteLine("No entries to verify.");
        Console.WriteLine("RESULT: PASS (empty log)");
        return;
    }

    bool valid = await repo.VerifyChainIntegrityAsync();

    if (valid)
    {
        Console.WriteLine("Hash chain: INTACT");
        Console.WriteLine("Ed25519 signatures: VALID");
        Console.WriteLine("Tampered rows: 0");
        Console.WriteLine("RESULT: PASS");
    }
    else
    {
        Console.WriteLine("RESULT: FAIL — audit log integrity compromised");
        Environment.ExitCode = CliExitCode.IntegrityFailure;
    }
}

/// <summary>
/// CLI: --baseline-reset — resets the global network baseline to Learning phase.
/// </summary>
static async Task BaselineResetAsync()
{
    Console.WriteLine("=== Network Baseline Reset ===");

    AgentConfiguration? config = LoadCliConfiguration();
    if (config is null) return;
    InstalledDatabase? db = OpenInstalledDatabase(config);
    if (db is null) return;

    using AgentDbContext context = db.Context;
    context.Database.Migrate();
    Console.WriteLine($"Database: {db.Path}");

    var service = new RansomGuard.Agent.Core.Detection.ExfilWatch.NetworkBaselineService(
        context,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RansomGuard.Agent.Core.Detection.ExfilWatch.NetworkBaselineService>.Instance);

    await service.ResetBaselineAsync("global", CancellationToken.None);
    Console.WriteLine("Network baseline reset to Learning phase.");
}

/// <summary>
/// CLI: --baseline-export — exports the current network baseline as JSON.
/// </summary>
static async Task BaselineExportAsync()
{
    Console.WriteLine("=== Network Baseline Export ===");

    AgentConfiguration? config = LoadCliConfiguration();
    if (config is null) return;
    InstalledDatabase? db = OpenInstalledDatabase(config);
    if (db is null) return;

    using AgentDbContext context = db.Context;
    context.Database.Migrate();
    Console.WriteLine($"Database: {db.Path}");

    var baselines = await context.NetworkBaselines.ToListAsync();
    var metrics = await context.NetworkBaselineMetrics.ToListAsync();

    var export = new
    {
        ExportedAt = DateTime.UtcNow,
        Baselines = baselines.Select(b => new
        {
            b.Scope, Phase = b.Phase.ToString(), b.LearningStartedAt,
            b.LearningCompletedAt, b.ObservationCount, b.ConfidenceScore
        }),
        Metrics = metrics.Select(m => new
        {
            MetricType = m.MetricType.ToString(), m.Dimension,
            m.HourlyAverageBytes, m.HourlyStdDevBytes, m.DailyAverageBytes,
            m.ObservationCount, m.ConfidenceScore
        })
    };

    string json = System.Text.Json.JsonSerializer.Serialize(export,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    string outputPath = Path.Combine(Environment.CurrentDirectory, "baseline-export.json");
    await File.WriteAllTextAsync(outputPath, json);
    Console.WriteLine($"Baseline exported to: {outputPath}");
    Console.WriteLine(json);
}

/// <summary>
/// CLI: --check-threat-intel-updates — checks for available threat intel update packages.
/// </summary>
static void CheckThreatIntelUpdates()
{
    Console.WriteLine("=== Threat Intel Update Check ===");

    string updateDir = EnvironmentVariableResolver.ResolvePath(
        "%ProgramData%\\RansomGuard-CM\\updates");

    var validator = new RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelUpdateValidator(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelUpdateValidator>.Instance,
        updateDir,
        EnvironmentVariableResolver.ResolvePath("%ProgramData%\\RansomGuard-CM\\threat-intel"));

    string? packagePath = validator.CheckForUpdates();
    if (packagePath is null)
    {
        Console.WriteLine("No update packages found.");
        Console.WriteLine($"Place packages in: {updateDir}");
        return;
    }

    Console.WriteLine($"Update package found: {packagePath}");
    Console.WriteLine("Run --apply-threat-intel-update to apply.");
}

/// <summary>
/// CLI: --apply-threat-intel-update — applies a signed threat intel update.
/// The installed database is opened BEFORE the update is applied: a missing database used to
/// leave the update applied with no audit entry -- an untraced change to detection behaviour.
/// </summary>
static async Task ApplyThreatIntelUpdateAsync()
{
    Console.WriteLine("=== Apply Threat Intel Update ===");

    AgentConfiguration? config = LoadCliConfiguration();
    if (config is null) return;

    string updateDir = EnvironmentVariableResolver.ResolvePath(
        "%ProgramData%\\RansomGuard-CM\\updates");
    string dataDir = EnvironmentVariableResolver.ResolvePath(
        "%ProgramData%\\RansomGuard-CM\\threat-intel");

    var validator = new RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelUpdateValidator(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelUpdateValidator>.Instance,
        updateDir, dataDir);

    string? packagePath = validator.CheckForUpdates();
    if (packagePath is null)
    {
        Console.WriteLine("No update packages found.");
        return;
    }

    // Open the audit trail first; refuse (nothing applied) if the database is missing.
    InstalledDatabase? db = OpenInstalledDatabase(config);
    if (db is null) return;
    using AgentDbContext context = db.Context;
    context.Database.Migrate();

    Console.WriteLine($"Package: {packagePath}");
    Console.WriteLine("Applying update...");

    var provider = new RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelDataLoader(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RansomGuard.Agent.Core.Detection.ThreatIntel.ThreatIntelDataLoader>.Instance);

    bool success = validator.Apply(packagePath, provider);
    if (success)
    {
        Console.WriteLine($"Update applied. New version: {provider.Version}");
        Console.WriteLine($"Tor exit nodes: {provider.TorExitNodeCount}");
        Console.WriteLine($"C2 servers: {provider.C2ServerCount}");
        Console.WriteLine($"LOLBAS binaries: {provider.LolbasBinaryCount}");

        var signer = new AuditLogSigner(db.KeyDirectory,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
        var repo = new AuditLogRepository(context, signer);

        await repo.AppendAsync("ThreatIntelUpdate",
            $"Applied threat intel update v{provider.Version} from {packagePath}",
            "ThreatIntel", Guid.NewGuid(), CancellationToken.None);

        Console.WriteLine($"Audit log entry created with Ed25519 signature in {db.Path}.");
    }
    else
    {
        Console.WriteLine("Update FAILED. Previous data restored from backup.");
        Environment.ExitCode = CliExitCode.IntegrityFailure;
    }
}

/// <summary>The installed agent database, opened by a CLI sub-command.</summary>
sealed record InstalledDatabase(AgentDbContext Context, string Path, string KeyDirectory);

/// <summary>CLI exit codes.</summary>
static class CliExitCode
{
    /// <summary>The operation failed (integrity check failed, update failed).</summary>
    public const int IntegrityFailure = 1;

    /// <summary>Refused: nothing was done (invalid configuration, database missing).</summary>
    public const int Refused = 2;
}
