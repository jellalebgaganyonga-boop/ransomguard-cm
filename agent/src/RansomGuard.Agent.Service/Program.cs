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

    Log.Information("RansomGuard-CM Agent starting up");

    var builder = Host.CreateApplicationBuilder(args);

    // Enable running as a Windows Service
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "RansomGuard-CM Agent";
    });

    // Configure Serilog from appsettings.json + programmatic config
    builder.Services.AddSerilog((services, loggerConfig) =>
    {
        var agentConfig = builder.Configuration
            .GetSection(AgentConfiguration.SectionName)
            .Get<AgentConfiguration>();

        string logPath = agentConfig?.Logging?.LogFilePath ?? "logs/agent-.log";
        logPath = EnvironmentVariableResolver.ResolvePath(logPath);
        int maxFileSizeMb = agentConfig?.Logging?.MaxFileSizeMB ?? 50;
        int retainedFiles = agentConfig?.Logging?.RetainedFileCount ?? 30;

        string? environment = agentConfig?.Identity?.Environment;
        bool isProduction = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);

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

    // Bind configuration section to strongly-typed options
    builder.Services.Configure<AgentConfiguration>(
        builder.Configuration.GetSection(AgentConfiguration.SectionName));

    // Register FluentValidation validator
    builder.Services.AddSingleton<IValidator<AgentConfiguration>, AgentConfigurationValidator>();

    // Register SQLite DbContext with SQLCipher encryption (CWE-311)
    var dbConnectionString = builder.Configuration
        .GetSection("Agent:Database:ConnectionString")
        .Value ?? "Data Source=agent.db";
    dbConnectionString = EnvironmentVariableResolver.ResolvePath(dbConnectionString);

    // Initialize SQLCipher provider
    SQLitePCL.Batteries_V2.Init();

    // Database encryption key via DPAPI
    string keyDir = EnvironmentVariableResolver.ResolvePath(
        builder.Configuration.GetSection("Agent:Database:KeyDirectory").Value
        ?? "%ProgramData%\\RansomGuard-CM\\keys");
    var dbKeyManager = new DatabaseKeyManager(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
    string dbKey = dbKeyManager.GetOrCreateKey();

    // Append Password to connection string for SQLCipher
    string encryptedConnectionString = dbConnectionString.Contains("Password=")
        ? dbConnectionString
        : $"{dbConnectionString};Password={dbKey}";

    builder.Services.AddDbContext<AgentDbContext>(options =>
        options.UseSqlite(encryptedConnectionString));

    // Register file event deduplicator
    var deduplicationWindowMs = builder.Configuration
        .GetSection("Agent:Detection:DeduplicationWindowMs")
        .Get<int>();
    if (deduplicationWindowMs <= 0) deduplicationWindowMs = 500;
    builder.Services.AddSingleton<IFileEventDeduplicator>(new FileEventDeduplicator(deduplicationWindowMs));

    // Register Ed25519 audit log signer
    var auditLogSigner = new AuditLogSigner(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
    builder.Services.AddSingleton(auditLogSigner);

    // Register repositories
    builder.Services.AddScoped<IDetectionEventRepository, DetectionEventRepository>();
    builder.Services.AddScoped<IAlertRepository, AlertRepository>();
    builder.Services.AddScoped<IAuditLogRepository>(sp =>
        new AuditLogRepository(sp.GetRequiredService<AgentDbContext>(), auditLogSigner));

    // Register SENTINEL services
    builder.Services.AddScoped<ISentinelCanaryRepository, SentinelCanaryRepository>();
    builder.Services.AddScoped<ICanaryFileService, CanaryFileService>();
    builder.Services.AddSingleton<RestartManagerHelper>();

    // Register ENTROPY services
    builder.Services.AddSingleton<IEntropyCalculator, EntropyCalculator>();

    // Register GENEALOGY services
    builder.Services.AddSingleton<IProcessSnapshotService, ProcessSnapshotService>();
    builder.Services.AddScoped<IGenealogyEnricher, GenealogyEnricher>();

    // Register rate limiting
    builder.Services.AddSingleton<RateLimiterFactory>();

    // Register USB GUARD services
    builder.Services.AddScoped<IUsbWhitelistService>(sp =>
        new UsbWhitelistService(
            sp.GetRequiredService<AgentDbContext>(),
            sp.GetRequiredService<ILogger<UsbWhitelistService>>(),
            System.Text.Encoding.UTF8.GetBytes(dbKey[..32])));
    builder.Services.AddSingleton<IMagicByteValidator, MagicByteValidator>();
    builder.Services.AddSingleton<AutorunInfDetector>();
    builder.Services.AddSingleton<SuspiciousLnkDetector>();
    builder.Services.AddSingleton<ArchiveScanner>();
    builder.Services.AddSingleton<UsbEntropyScanner>();
    builder.Services.AddScoped<IUsbContentScanner, UsbContentScanner>();
    builder.Services.AddSingleton<BootableUsbDetector>();
    builder.Services.AddScoped<IUsbActionEngine, UsbActionEngine>();
    builder.Services.AddSingleton<IWmiEventSubscriber, WmiEventSubscriber>();

    // Register anti-tampering services
    builder.Services.AddSingleton<IAgentProtector, AgentProtector>();
    builder.Services.AddSingleton<RegistryWatcher>();
    builder.Services.AddSingleton<DebuggerDetector>();
    builder.Services.AddSingleton<CodeSectionIntegrity>();

    // SENTINEL deployment runs before Worker to ensure canaries exist
    builder.Services.AddHostedService<SentinelDeploymentService>();
    builder.Services.AddHostedService<SentinelMonitor>();
    builder.Services.AddHostedService<EntropyMonitor>();

    // USB GUARD monitor
    builder.Services.AddHostedService<UsbDeviceMonitor>();

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();

    // Fail-fast: validate configuration at startup
    ValidateConfiguration(host.Services);

    // Ensure database directory exists and apply migrations
    EnsureDatabase(host.Services);

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RansomGuard-CM Agent terminated unexpectedly");
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
/// Verifies the audit log hash chain and Ed25519 signatures.
/// Exit 0 if valid, exit 1 if tampered.
/// </summary>
static async Task VerifyAuditLogAsync()
{
    Console.WriteLine("=== Audit Log Verification ===");

    var builder = Host.CreateApplicationBuilder([]);

    var dbConnectionString = builder.Configuration
        .GetSection("Agent:Database:ConnectionString")
        .Value ?? "Data Source=agent.db";
    dbConnectionString = EnvironmentVariableResolver.ResolvePath(dbConnectionString);

    SQLitePCL.Batteries_V2.Init();

    string keyDir = EnvironmentVariableResolver.ResolvePath(
        builder.Configuration.GetSection("Agent:Database:KeyDirectory").Value
        ?? "%ProgramData%\\RansomGuard-CM\\keys");
    var dbKeyManager = new DatabaseKeyManager(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
    string dbKey = dbKeyManager.GetOrCreateKey();

    string encryptedConnectionString = dbConnectionString.Contains("Password=")
        ? dbConnectionString
        : $"{dbConnectionString};Password={dbKey}";

    var options = new DbContextOptionsBuilder<AgentDbContext>()
        .UseSqlite(encryptedConnectionString)
        .Options;

    using var context = new AgentDbContext(options);
    context.Database.Migrate();

    var signer = new AuditLogSigner(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
    var repo = new AuditLogRepository(context, signer);

    int entryCount = await context.AuditLogs.CountAsync();
    Console.WriteLine($"Audit log entries: {entryCount}");

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
        Environment.ExitCode = 1;
    }
}

/// <summary>
/// CLI: --baseline-reset — resets the global network baseline to Learning phase.
/// </summary>
static async Task BaselineResetAsync()
{
    Console.WriteLine("=== Network Baseline Reset ===");

    var builder = Host.CreateApplicationBuilder([]);

    var dbConnectionString = builder.Configuration
        .GetSection("Agent:Database:ConnectionString")
        .Value ?? "Data Source=agent.db";
    dbConnectionString = EnvironmentVariableResolver.ResolvePath(dbConnectionString);

    SQLitePCL.Batteries_V2.Init();

    string keyDir = EnvironmentVariableResolver.ResolvePath(
        builder.Configuration.GetSection("Agent:Database:KeyDirectory").Value
        ?? "%ProgramData%\\RansomGuard-CM\\keys");
    var dbKeyManager = new DatabaseKeyManager(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
    string dbKey = dbKeyManager.GetOrCreateKey();

    string encryptedConnectionString = dbConnectionString.Contains("Password=")
        ? dbConnectionString
        : $"{dbConnectionString};Password={dbKey}";

    var options = new DbContextOptionsBuilder<AgentDbContext>()
        .UseSqlite(encryptedConnectionString)
        .Options;

    using var context = new AgentDbContext(options);
    context.Database.Migrate();

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

    var builder = Host.CreateApplicationBuilder([]);

    var dbConnectionString = builder.Configuration
        .GetSection("Agent:Database:ConnectionString")
        .Value ?? "Data Source=agent.db";
    dbConnectionString = EnvironmentVariableResolver.ResolvePath(dbConnectionString);

    SQLitePCL.Batteries_V2.Init();

    string keyDir = EnvironmentVariableResolver.ResolvePath(
        builder.Configuration.GetSection("Agent:Database:KeyDirectory").Value
        ?? "%ProgramData%\\RansomGuard-CM\\keys");
    var dbKeyManager = new DatabaseKeyManager(keyDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
    string dbKey = dbKeyManager.GetOrCreateKey();

    string encryptedConnectionString = dbConnectionString.Contains("Password=")
        ? dbConnectionString
        : $"{dbConnectionString};Password={dbKey}";

    var options = new DbContextOptionsBuilder<AgentDbContext>()
        .UseSqlite(encryptedConnectionString)
        .Options;

    using var context = new AgentDbContext(options);
    context.Database.Migrate();

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
/// Validates the agent configuration at startup. Throws if invalid (fail-fast principle).
/// </summary>
static void ValidateConfiguration(IServiceProvider services)
{
    var configuration = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgentConfiguration>>();
    var validator = services.GetRequiredService<IValidator<AgentConfiguration>>();

    AgentConfiguration config = configuration.CurrentValue;
    FluentValidation.Results.ValidationResult result = validator.Validate(config);

    if (!result.IsValid)
    {
        var errors = string.Join(Environment.NewLine, result.Errors.Select(e => $"  - {e.PropertyName}: {e.ErrorMessage}"));
        throw new InvalidOperationException(
            $"Agent configuration is invalid. The service cannot start.{Environment.NewLine}{errors}");
    }
}
