using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Security;
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

    // Register repositories
    builder.Services.AddScoped<IDetectionEventRepository, DetectionEventRepository>();
    builder.Services.AddScoped<IAlertRepository, AlertRepository>();
    builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

    // Register SENTINEL services
    builder.Services.AddScoped<ISentinelCanaryRepository, SentinelCanaryRepository>();
    builder.Services.AddScoped<ICanaryFileService, CanaryFileService>();
    builder.Services.AddSingleton<RestartManagerHelper>();

    // SENTINEL deployment runs before Worker to ensure canaries exist
    builder.Services.AddHostedService<SentinelDeploymentService>();
    builder.Services.AddHostedService<SentinelMonitor>();
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
