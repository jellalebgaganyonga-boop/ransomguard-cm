using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Detection.IndicatorRemoval;
using RansomGuard.Agent.Core.Detection.ThreatIntel;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security;
using RansomGuard.Agent.Core.Security.AntiTampering;
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

        // Cross-module event bus
        services.AddSingleton<IDetectionEventBus, InMemoryDetectionEventBus>();

        // EXFIL WATCH — Network baseline
        services.AddScoped<INetworkBaselineService, NetworkBaselineService>();

        // EXFIL WATCH — Threat intelligence (loaded from embedded JSON data)
        services.AddSingleton<ThreatIntelDataLoader>();
        services.AddSingleton<IThreatIntelProvider>(sp => sp.GetRequiredService<ThreatIntelDataLoader>());

        // EXFIL WATCH — Detection rules (DI-dependent)
        services.AddSingleton<IExfilDetectionRule, LolbasExfilRule>();

        // EXFIL WATCH — Rule engine
        services.AddScoped<ExfilRuleEngine>();

        // EXFIL WATCH — Action engine (B.6)
        services.AddSingleton<IFirewallManager, WindowsFirewallManager>();
        services.AddSingleton<IProcessThrottler, WindowsProcessThrottler>();
        services.AddScoped<AlertOnlyAction>();
        services.AddScoped<ThrottleProcessAction>();
        services.AddScoped<BlockIpAction>();
        services.AddScoped<IExfilActionEngine>(sp =>
        {
            var config = sp.GetRequiredService<IOptionsMonitor<AgentConfiguration>>().CurrentValue;
            return new ExfilActionEngine(
                sp.GetRequiredService<AlertOnlyAction>(),
                sp.GetRequiredService<ThrottleProcessAction>(),
                sp.GetRequiredService<BlockIpAction>(),
                sp.GetRequiredService<IThreatIntelProvider>(),
                sp.GetRequiredService<IDataVolumeTracker>(),
                sp.GetRequiredService<IAuditLogRepository>(),
                config.ExfilWatch ?? new ExfilWatchOptions(),
                sp.GetRequiredService<ILogger<ExfilActionEngine>>());
        });

        // INDICATOR REMOVAL — Detectors
        services.AddSingleton<IIndicatorRemovalDetector, EventLogClearingDetector>();
        services.AddSingleton<IIndicatorRemovalDetector, UsnJournalClearingDetector>();
        services.AddSingleton<IIndicatorRemovalDetector, DefenderTamperingDetector>();
        services.AddSingleton<IIndicatorRemovalDetector, SchedTaskTamperingDetector>();
        services.AddSingleton<MultiStageKillChainDetector>();

        // ENTROPY
        services.AddSingleton<IEntropyCalculator, EntropyCalculator>();

        // GENEALOGY
        services.AddSingleton<IProcessSnapshotService, ProcessSnapshotService>();
        services.AddScoped<IGenealogyEnricher, GenealogyEnricher>();

        // USB GUARD
        AddUsbGuardServices(services, dbKey);

        // Anti-tampering
        services.AddSingleton<IAgentProtector, AgentProtector>();
        services.AddSingleton<RegistryWatcher>();
        services.AddSingleton<DebuggerDetector>();
        services.AddSingleton<CodeSectionIntegrity>();
    }

    /// <summary>
    /// Registers all USB GUARD module services.
    /// </summary>
    public static void AddUsbGuardServices(IServiceCollection services, string dbKey)
    {
        // Salt derived from DB key (first 32 bytes)
        byte[] salt = System.Text.Encoding.UTF8.GetBytes(dbKey.Length >= 32 ? dbKey[..32] : dbKey.PadRight(32, '0'));

        // Whitelist
        services.AddScoped<IUsbWhitelistService>(sp =>
            new UsbWhitelistService(
                sp.GetRequiredService<AgentDbContext>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UsbWhitelistService>>(),
                salt));

        // Scanners
        services.AddSingleton<IMagicByteValidator, MagicByteValidator>();
        services.AddSingleton<AutorunInfDetector>();
        services.AddSingleton<SuspiciousLnkDetector>();
        services.AddSingleton<ArchiveScanner>();
        services.AddSingleton<UsbEntropyScanner>();
        services.AddScoped<IUsbContentScanner, UsbContentScanner>();

        // Bootable detection
        services.AddSingleton<BootableUsbDetector>();

        // Actions
        services.AddScoped<IUsbActionEngine, UsbActionEngine>();

        // WMI subscriber
        services.AddSingleton<IWmiEventSubscriber, WmiEventSubscriber>();
    }
}
