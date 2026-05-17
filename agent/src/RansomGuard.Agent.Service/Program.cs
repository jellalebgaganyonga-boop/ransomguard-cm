using FluentValidation;
using RansomGuard.Agent.Core.Configuration;
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

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();

    // Fail-fast: validate configuration at startup
    ValidateConfiguration(host.Services);

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
