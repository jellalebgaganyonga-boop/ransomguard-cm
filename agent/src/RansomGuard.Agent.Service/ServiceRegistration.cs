using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Shared service registration used by both Program.cs and integration tests.
/// Extracted to enable IHost-based E2E testing with identical DI configuration.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers all RansomGuard agent services into the DI container.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<AgentConfiguration>(
            configuration.GetSection(AgentConfiguration.SectionName));

        // FluentValidation
        services.AddSingleton<IValidator<AgentConfiguration>, AgentConfigurationValidator>();

        // Database
        var dbConnectionString = configuration
            .GetSection("Agent:Database:ConnectionString")
            .Value ?? "Data Source=agent.db";
        dbConnectionString = EnvironmentVariableResolver.ResolvePath(dbConnectionString);

        SQLitePCL.Batteries_V2.Init();

        string keyDir = EnvironmentVariableResolver.ResolvePath(
            configuration.GetSection("Agent:Database:KeyDirectory").Value
            ?? "%ProgramData%\\RansomGuard-CM\\keys");

        var dbKeyManager = new DatabaseKeyManager(keyDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
        string dbKey = dbKeyManager.GetOrCreateKey();

        string encryptedConnectionString = dbConnectionString.Contains("Password=")
            ? dbConnectionString
            : $"{dbConnectionString};Password={dbKey}";

        services.AddDbContext<AgentDbContext>(options =>
            options.UseSqlite(encryptedConnectionString));

        // Deduplicator
        var deduplicationWindowMs = configuration
            .GetSection("Agent:Detection:DeduplicationWindowMs")
            .Get<int>();
        if (deduplicationWindowMs <= 0) deduplicationWindowMs = 500;
        services.AddSingleton<IFileEventDeduplicator>(new FileEventDeduplicator(deduplicationWindowMs));

        // Ed25519 audit signer
        var auditLogSigner = new AuditLogSigner(keyDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
        services.AddSingleton(auditLogSigner);

        // Repositories
        services.AddScoped<IDetectionEventRepository, DetectionEventRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IAuditLogRepository>(sp =>
            new AuditLogRepository(sp.GetRequiredService<AgentDbContext>(), auditLogSigner));

        // SENTINEL
        services.AddScoped<ISentinelCanaryRepository, SentinelCanaryRepository>();
        services.AddScoped<ICanaryFileService, CanaryFileService>();
        services.AddSingleton<RestartManagerHelper>();

        // Rate limiting
        services.AddSingleton<RateLimiterFactory>();

        // ENTROPY
        services.AddSingleton<IEntropyCalculator, EntropyCalculator>();

        // GENEALOGY
        services.AddSingleton<IProcessSnapshotService, ProcessSnapshotService>();
        services.AddScoped<IGenealogyEnricher, GenealogyEnricher>();
    }
}
