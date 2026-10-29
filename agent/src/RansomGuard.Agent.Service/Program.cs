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
using RansomGuard.Agent.Core.Diagnostics;
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
using SecurityIdentifier = System.Security.Principal.SecurityIdentifier;

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

    // Rooted at the executable's directory: a Windows service starts in System32 (AGT-SVC-001).
    var builder = AgentHost.CreateBuilder(args);
    Log.Information("Host environment: {Environment}, content root: {ContentRoot}",
        builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);

    // Validate the configuration ONCE, before anything consumes it (logging, services,
    // database): everything below reads this validated object, never raw IConfiguration.
    // A missing appsettings.json is a refusal, never a start on default values.
    AgentConfigurationLoadResult load = AgentHost.LoadConfiguration(builder);
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

    // AGT-KEY-001: close the data directories (database, keys, logs) to SYSTEM and Administrators
    // before anything opens or creates a file there; a protection left open refuses the start.
    if (!ProtectAgentData(agentConfig, builder.Environment.EnvironmentName))
    {
        Environment.ExitCode = 1;
        return;
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

        // File sink: daily rotation, size limit, retained count.
        //
        // `shared: false` and an explicit flush interval, both deliberate. On PC-RECEPTION on
        // 2026-10-26 the agent ran, enrolled and detected for twenty minutes while
        // agent-20261026.log stayed 0 bytes -- and the owner guide sends people to that file when
        // something goes wrong, so there was nothing to find. Only this process writes the log,
        // so the shared-writer path buys nothing and is the part that was not reaching the disc;
        // one second of buffering is the most that can now be lost if the agent is killed.
        if (isProduction)
        {
            loggerConfig.WriteTo.File(
                new CompactJsonFormatter(),
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: maxFileSizeMb * 1024L * 1024L,
                retainedFileCountLimit: retainedFiles,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1));
        }
        else
        {
            loggerConfig.WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: maxFileSizeMb * 1024L * 1024L,
                retainedFileCountLimit: retainedFiles,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
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

    // Development only: stop a detector on demand, through its real failure path (LOT 2 demo).
    if (ServiceRegistration.AddModuleFaultInjection(builder.Services, builder.Configuration, builder.Environment.EnvironmentName))
    {
        Log.Warning(ModuleFaultInjectionPolicy.ActiveWarning);
    }

    var host = builder.Build();

    // Nothing slow between Build() and Run(). Creating the database, applying its migrations and
    // chaining the audit entries a previous stop left waiting all used to happen here -- before
    // Windows was told the service was running. On a first-ever install that is where the schema
    // is created and Entity Framework is compiled for the first time, and it took over two
    // minutes on PC-RECEPTION: `sc start` returned 1053 and only the installer's retry saved the
    // installation. DatabaseStartupService does that work now, as the first hosted service, and
    // everything that reads the database waits on DatabaseReady.
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

// The database is created, migrated and announced by DatabaseStartupService, and the audit
// entries a previous stop left waiting are chained there too. Both used to happen here, between
// Build() and Run(), where Windows was still waiting to be told the service had started.

/// <summary>
/// Reports a startup refusal where it is always seen: the console, always; the Windows Event
/// Log, best effort only. Writing to the Event Log needs a registered source (elevation on
/// first use); if that fails, the failure must never hide the message that explains why the
/// agent refuses to start.
/// </summary>
static void ReportStartupRefusal(IReadOnlyList<string> errors, string reason = "its configuration is invalid")
{
    string message = $"RansomGuard-CM Agent refuses to start: {reason}."
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
/// AGT-KEY-001: closes the agent's data directories and verifies them. A directory that is still
/// open after the attempt is a refusal: a security agent never runs with its keys readable by
/// every local account. In Development only, the account running the agent keeps access, with a
/// Warning at every start.
/// </summary>
static bool ProtectAgentData(AgentConfiguration config, string environmentName)
{
    using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);
    SecurityIdentifier account = System.Security.Principal.WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The account running the agent has no SID.");
    DataProtectionPolicy policy = DataProtectionPolicy.For(environmentName, account);

    DataProtectionResult result = AgentDataProtection.Protect(
        config, policy, new DaclEnforcer(loggerFactory.CreateLogger<DaclEnforcer>()));
    if (result.Errors.Count > 0)
    {
        // One line per entry can mean hundreds of log files: the first 20, then the count.
        const int shown = 20;
        IReadOnlyList<string> lines = result.Errors.Count <= shown
            ? result.Errors
            : [.. result.Errors.Take(shown), $"... and {result.Errors.Count - shown} more"];
        ReportStartupRefusal(lines, "its data directories cannot be closed to SYSTEM and Administrators");
        return false;
    }

    if (policy.AddsDevelopmentAccount)
    {
        Log.Warning("Development: the account {Account} has full control of the agent's data directories {Roots}. "
            + "Never in Production, where only SYSTEM and Administrators do.",
            account.Translate(typeof(System.Security.Principal.NTAccount)).Value, result.Roots);
    }

    return true;
}

/// <summary>
/// CLI: loads the configuration exactly as the service does (validated once), rooted at the
/// executable's directory -- never the caller's working directory. Exit code 2 when refused.
/// </summary>
static AgentConfiguration? LoadCliConfiguration()
{
    var builder = AgentHost.CreateBuilder([]);

    AgentConfigurationLoadResult load = AgentHost.LoadConfiguration(builder);
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
/// Exit 0 if valid, 1 if the integrity check fails, 2 if refused (nothing was done),
/// 3 if the chain is empty (INCONCLUSIVE: a fresh install and an erased chain look the same).
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

    var (verdict, entryCount) = await AuditChainVerification.VerifyAsync(context, repo);
    Console.WriteLine($"Database examined: {db.Path}");
    Console.WriteLine($"Audit log entries read: {entryCount}");
    // Read only (AGT-AUD-002): recorded entries not chained yet. The agent chains them first at
    // its next start; they are not covered by the chain until then.
    int waiting = await context.PendingAuditEntries.CountAsync();
    Console.WriteLine($"Audit entries recorded, waiting to be chained: {waiting}");

    switch (verdict)
    {
        case AuditChainVerdict.Pass:
            Console.WriteLine("Hash chain: INTACT");
            Console.WriteLine("Ed25519 signatures: VALID");
            Console.WriteLine("Tampered rows: 0");
            Console.WriteLine("RESULT: PASS");
            break;

        case AuditChainVerdict.Inconclusive:
            // A fresh install and an erased chain both have zero entries: never PASS.
            Console.WriteLine("RESULT: INCONCLUSIVE — the audit chain is empty.");
            Console.WriteLine("A fresh install and a chain wiped by an attacker look the same locally;");
            Console.WriteLine("only the GRID's copy of the chain can tell them apart.");
            Environment.ExitCode = CliExitCode.Inconclusive;
            break;

        default:
            Console.WriteLine("RESULT: FAIL — audit log integrity compromised");
            Environment.ExitCode = CliExitCode.IntegrityFailure;
            break;
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

    /// <summary>--verify-audit-log on an empty chain: nothing can be concluded locally.</summary>
    public const int Inconclusive = AuditChainVerification.InconclusiveExitCode;
}
