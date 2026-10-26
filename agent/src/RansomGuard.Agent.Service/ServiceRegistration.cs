using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Detection.IndicatorRemoval;
using RansomGuard.Agent.Core.Detection.ThreatIntel;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;
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
    /// <summary>
    /// Registers the detection monitors as hosted services, in start order. Kept here rather
    /// than in Program.cs so the full set of agent services can be built in tests, with scope
    /// validation, exactly as the agent builds them.
    /// </summary>
    public static void AddMonitors(IServiceCollection services)
    {
        // First: the database, which every monitor below waits for. Registered first so it is the
        // first ExecuteAsync to run, and done here rather than before host.Run() so that Windows
        // is told the service is running without waiting for a schema to be created.
        services.AddHostedService<DatabaseStartupService>();

        // SENTINEL deployment runs before Worker to ensure canaries exist
        services.AddHostedService<SentinelDeploymentService>();
        services.AddHostedService<SentinelMonitor>();
        // The suspended processes (ADR-026): ended ones closed, undecided ones escalated.
        services.AddHostedService<SuspensionWatchService>();
        services.AddHostedService<EntropyMonitor>();
        // GENEALOGY: feeds the ancestry store from the session and owns the module's state.
        services.AddHostedService<GenealogyMonitor>();

        // USB GUARD monitor
        services.AddHostedService<UsbDeviceMonitor>();

        // EXFIL WATCH monitor + firewall rule cleanup
        services.AddHostedService<ExfilWatchMonitor>();
        services.AddHostedService<ExfilFirewallRuleCleanupService>();

        services.AddHostedService<Worker>();
    }

    /// <summary>
    /// Replaces the injector with the Development one when, and only when, the host environment is
    /// Development and Agent:Development:FaultInjection is true. Returns whether it is active.
    /// </summary>
    public static bool AddModuleFaultInjection(IServiceCollection services, IConfiguration configuration, string? environmentName)
    {
        if (!ModuleFaultInjectionPolicy.IsEnabled(configuration, environmentName))
        {
            return false;
        }

        services.RemoveAll<IModuleFaultInjector>();
        services.AddSingleton<IModuleFaultInjector, DevelopmentModuleFaultInjector>(sp =>
            new DevelopmentModuleFaultInjector(sp.GetRequiredService<ILogger<DevelopmentModuleFaultInjector>>()));
        return true;
    }

    /// <summary>
    /// One service failing does not take the protection off the workstation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET's default is <see cref="BackgroundServiceExceptionBehavior.StopHost"/>: an unhandled
    /// exception in any one <see cref="BackgroundService"/> stops the host, and therefore every
    /// other service in it. For a web application that is right. For this agent it is not: the
    /// process exits with code 0, so nothing reads it as a crash and Windows does not restart it,
    /// and the workstation is left unwatched until somebody notices the console says disconnected.
    /// </para>
    /// <para>
    /// On PC-LAB on 2026-10-26 the GRID took longer than the client's 10 s to answer a poll for
    /// commands. The timeout escaped GridCommandService -- fixed there too -- and SENTINEL,
    /// ENTROPY, GENEALOGY and USB GUARD all stopped with it, fifty minutes before anybody looked.
    /// </para>
    /// <para>
    /// The failure is not hidden: the host logs it at Error with the service that threw, which
    /// the Event Log sink carries, and a detection module that dies publishes its own module
    /// state so the console shows it as failed rather than active. What changes is that the rest
    /// of the agent keeps watching.
    /// </para>
    /// </remarks>
    public static void KeepTheAgentRunningWhenOneServiceFails(IServiceCollection services)
    {
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        KeepTheAgentRunningWhenOneServiceFails(services);

        // Bind configuration
        services.Configure<AgentConfiguration>(
            configuration.GetSection(AgentConfiguration.SectionName));

        // FluentValidation
        services.AddSingleton<IValidator<AgentConfiguration>, AgentConfigurationValidator>();

        // One source: the bound AgentConfiguration (validated once by AgentConfigurationLoader
        // in Program.cs). No raw read with its own ?? fallback: defaults live on the options.
        AgentConfiguration agentConfig = configuration.GetSection(AgentConfiguration.SectionName).Get<AgentConfiguration>()
            ?? throw new InvalidOperationException($"The '{AgentConfiguration.SectionName}' configuration section is missing.");

        // Database
        string dbConnectionString = EnvironmentVariableResolver.ResolvePath(agentConfig.Database.ConnectionString);

        SQLitePCL.Batteries_V2.Init();

        string keyDir = EnvironmentVariableResolver.ResolvePath(agentConfig.Database.KeyDirectory);

        var dbKeyManager = new DatabaseKeyManager(keyDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseKeyManager>.Instance);
        string dbKey = dbKeyManager.GetOrCreateKey();

        string encryptedConnectionString = dbConnectionString.Contains("Password=")
            ? dbConnectionString
            : $"{dbConnectionString};Password={dbKey}";

        // Durability (AGT-AUD-002): a recorded audit entry is one committed INSERT into
        // PendingAuditEntries, chained later. It survives a power cut only if the commit is
        // flushed to disk. journal_mode=WAL and synchronous=FULL are set explicitly on every
        // connection (AgentConnectionSettings) — no silent default: WAL used to come from EF
        // Core creating the file, FULL from the SQLCipher build. In WAL mode, synchronous MUST
        // stay FULL: NORMAL may lose the last commits on a power cut. Never lower it;
        // AgentDatabaseDurabilityTests fails if either changes.
        services.AddDbContext<AgentDbContext>(options =>
            options.UseSqlite(encryptedConnectionString).AddInterceptors(new AgentConnectionSettings()));

        // Deduplicator (DetectionOptions default 500 ms, validated range 50-5000): one per consumer
        // (V5). Transient on purpose: the Worker, SENTINEL, ENTROPY and USB GUARD each watch with
        // their own filter. Until 2026-10-21 one Singleton served them all: within the window, the first
        // watcher to handle a write takes the slot and the others do not get it. Possible by construction;
        // the loss was not observed in CI (FileEventDeduplicationPerConsumerTests, 20/20 on the old code).
        // A filter only folds the bursts of ONE watcher.
        services.AddTransient<IFileEventDeduplicator>(
            _ => new FileEventDeduplicator(agentConfig.Detection.DeduplicationWindowMs));

        // Ed25519 audit signer
        var auditLogSigner = new AuditLogSigner(keyDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditLogSigner>.Instance);
        services.AddSingleton(auditLogSigner);

        // Repositories
        services.AddScoped<IDetectionEventRepository, DetectionEventRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        // Announced by DatabaseStartupService, awaited by everything that reads the database.
        services.AddSingleton<DatabaseReady>();
        // Audit (AGT-AUD-002): recording is one INSERT, chaining happens in the background.
        services.AddSingleton<AuditHealth>();
        services.AddSingleton<AuditChainWriter>();
        services.AddSingleton<IAuditChainWriter>(sp => sp.GetRequiredService<AuditChainWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<AuditChainWriter>());
        services.AddScoped<IAuditLogRepository>(sp =>
            new AuditLogRepository(
                sp.GetRequiredService<AgentDbContext>(),
                auditLogSigner,
                sp.GetRequiredService<IAuditChainWriter>(),
                sp.GetRequiredService<AuditHealth>(),
                sp.GetRequiredService<ILogger<AuditLogRepository>>()));

        // SENTINEL
        services.AddScoped<ISentinelCanaryRepository, SentinelCanaryRepository>();
        services.AddScoped<ICanaryFileService, CanaryFileService>();
        services.AddSingleton<RestartManagerHelper>();
        services.AddSingleton(sp => new EtwFileIoSession(
            () => new TraceEventKernelFileIoTrace(),
            sp.GetRequiredService<ILogger<EtwFileIoSession>>(),
            TimeProvider.System,
            scopeFactory: sp.GetRequiredService<IServiceScopeFactory>()));

        // One kernel FileIO session, several readers (AGT-ATTR-001). SENTINEL opens the session and
        // subscribes its canary filter; ENTROPY subscribes the attribution cache. Both singletons,
        // because they must be the same instances the two modules share.
        services.AddSingleton<ProcessCreationTimes>();
        services.AddSingleton<FileIoFanout>();
        services.AddSingleton(sp => new FileWriteAttributionCache(sp.GetRequiredService<ProcessCreationTimes>()));

        // GENEALOGY's source: the ancestry of a writer, captured while it is still running. The
        // Restart Manager it used before sees nobody once the writer has closed its handle.
        services.AddSingleton<ProcessAncestryStore>();

        // The paths the agent writes itself, shared by the deployment service (which plants the
        // decoys) and the monitor (which watches them), so a fresh install raises no alert.
        services.AddSingleton<AgentOwnWrites>();

        // Rate limiting
        services.AddSingleton<RateLimiterFactory>();

        // Runtime module-state registry: each module publishes its real state here
        // and the heartbeat reads it, instead of deriving status from config.
        services.AddSingleton<IModuleStateRegistry, InMemoryModuleStateRegistry>();
        // One reporter for every module: registry, Critical log, audit — and the
        // per-minute limit on ModuleEventsLost entries.
        services.AddSingleton<IModuleFailureReporter, ModuleFailureReporter>();
        // Never stops anything; replaced only in Development, see AddModuleFaultInjection.
        services.AddSingleton<IModuleFaultInjector, NoModuleFaultInjector>();
        // Monotonic clock (Stopwatch) for uptime and upload ages, and timers; fakeable in tests.
        services.AddSingleton(TimeProvider.System);
        // %EACHUSERPROFILE% (AGT-PATH-001): the real users of the machine, read from ProfileList.
        services.AddSingleton(UserProfileEnumerator.Windows);

        // Cross-module event bus
        services.AddSingleton<IDetectionEventBus, InMemoryDetectionEventBus>();

        // EXFIL WATCH — Network baseline
        services.AddScoped<INetworkBaselineService, NetworkBaselineService>();
        services.AddSingleton<INetworkActivityMonitor, EtwNetworkCapture>();

        // EXFIL WATCH — Threat intelligence (loaded from embedded JSON data)
        services.AddSingleton<ThreatIntelDataLoader>();
        services.AddSingleton<IThreatIntelProvider>(sp => sp.GetRequiredService<ThreatIntelDataLoader>());

        // EXFIL WATCH — Detection rules (DI-dependent)
        services.AddSingleton<IExfilDetectionRule, LolbasExfilRule>();

        // EXFIL WATCH — Data volume tracker: a Singleton on purpose. The monitor opens a scope per
        // network event; Scoped, every event would get an empty tracker and no sliding window or
        // baseline would ever fill. Until 2026-10-21 it was registered nowhere: the consumer
        // resolved null and skipped every rule while the module read active (EXFIL-REG-001).
        services.AddScoped<IExfilLearningPhaseStore, ExfilLearningPhaseStore>();
        services.AddSingleton<IDataVolumeTracker>(sp =>
        {
            int days = (sp.GetRequiredService<IOptionsMonitor<AgentConfiguration>>().CurrentValue.ExfilWatch
                ?? new ExfilWatchOptions()).LearningPhaseDays;
            // The learning start is read from the database, not from the clock: a restart must not
            // begin the seven days again (AGT-EXF-001). This Singleton is built once, so one scope
            // here is enough; the first run of the module writes the row.
            using IServiceScope scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
            DateTime startedAt = scope.ServiceProvider.GetRequiredService<IExfilLearningPhaseStore>().StartedAtOrBegin(days);
            return new DataVolumeTracker(sp.GetRequiredService<ILogger<DataVolumeTracker>>(), days, startedAt);
        });

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
                sp.GetRequiredService<ILogger<ExfilActionEngine>>(),
                sp.GetService<IGenealogyEnricher>());
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

        // IRONCLAD
        AddIronCladServices(services, configuration);

        // GRID server communication
        AddGridServices(services);

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

        // Actions (real since 2026-10-22, AGT-USB-001): the Windows calls for read-only and eject, and the
        // quarantine store (AES-256-GCM, QuarantineService) under %ProgramData%, keyed from the database
        // key (a derivation, never the key itself). The engine takes both; without them it says so in
        // every result (no_device_control / no_quarantine_service) instead of claiming an action.
        services.AddSingleton<IUsbDeviceControl, WindowsUsbDeviceControl>();
        byte[] quarantineKey = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("RansomGuard-CM quarantine:" + dbKey));
        string quarantineDir = EnvironmentVariableResolver.ResolvePath(@"%ProgramData%\RansomGuard-CM\quarantine");
        services.AddScoped<IQuarantineService>(sp => new QuarantineService(
            sp.GetRequiredService<AgentDbContext>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QuarantineService>>(),
            quarantineDir,
            quarantineKey));
        services.AddScoped<IUsbActionEngine>(sp => new UsbActionEngine(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UsbActionEngine>>(),
            sp.GetRequiredService<IAuditLogRepository>(),
            sp.GetService<RansomGuard.Agent.Core.Detection.IronClad.Actions.IIronCladActionEngine>(),
            sp.GetRequiredService<IUsbDeviceControl>(),
            sp.GetRequiredService<IQuarantineService>()));

        // WMI subscriber
        services.AddSingleton<IWmiEventSubscriber, WmiEventSubscriber>();
    }

    /// <summary>
    /// Registers GRID server communication services (HttpClient, enrollment, heartbeat, command polling).
    /// </summary>
    public static void AddGridServices(IServiceCollection services)
    {
        services.AddHttpClient(GridApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AgentConfiguration>>().Value;
            var handler = new HttpClientHandler();

            // Dev mode: trust self-signed server certificates
            if (config.Server.TrustAnyCertificate)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        });

        services.AddSingleton<GridApiClient>();
        services.AddSingleton<IGridApiClient>(sp => sp.GetRequiredService<GridApiClient>());
        services.AddSingleton<EnrollmentStateManager>();
        services.AddSingleton<EnrollmentService>();
        services.AddHostedService(sp => sp.GetRequiredService<EnrollmentService>());
        services.AddSingleton<GridHeartbeatService>();
        services.AddHostedService(sp => sp.GetRequiredService<GridHeartbeatService>());
        services.AddHostedService<GridCommandService>();

        // Alert forwarding (the bridge: agent detections → GRID dashboard)
        services.AddSingleton<AlertForwardingService>();
        services.AddSingleton<IAlertForwardingQueue>(sp => sp.GetRequiredService<AlertForwardingService>());

        // SENTINEL response (ADR-026): NtSuspendProcess on a resolved attribution, never on a guess.
        services.AddSingleton<IProcessSuspender, NtProcessSuspender>();
        services.AddSingleton(sp => new CanaryResponseEngine(
            sp.GetRequiredService<IProcessSuspender>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IAlertForwardingQueue>(),
            sp.GetRequiredService<IOptionsMonitor<AgentConfiguration>>().CurrentValue.Sentinel?.Response,
            sp.GetRequiredService<ILogger<CanaryResponseEngine>>(),
            TimeProvider.System));
        services.AddHostedService(sp => sp.GetRequiredService<AlertForwardingService>());
    }

    /// <summary>
    /// Registers IronClad hardware response module services.
    /// Conditionally registers MockArduinoServer only in TcpMock mode.
    /// </summary>
    public static void AddIronCladServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IronCladOptions>(configuration.GetSection("IronClad"));

        // Documented fallback, kept on purpose: an absent IronClad section means the module is
        // off (IronCladOptions.Enabled defaults to false), which is the safe state. Its
        // CommunicationMode, when present, is validated by AgentConfigurationLoader.
        var ironCladOptions = configuration.GetSection("IronClad").Get<IronCladOptions>() ?? new IronCladOptions();

        // Communicators
        services.AddTransient<TcpMockCommunicator>();
        services.AddTransient(sp => new SerialPortCommunicator(ironCladOptions));
        services.AddSingleton<IIronCladCommunicatorFactory>(sp =>
            new IronCladCommunicatorFactory(ironCladOptions, sp));
        services.AddSingleton<IIronCladCommunicator>(sp =>
            sp.GetRequiredService<IIronCladCommunicatorFactory>().Create());

        // Action engine
        services.AddScoped<IIronCladActionEngine, IronCladActionEngine>();

        // Repositories
        services.AddScoped<IIronCladEventRepository, IronCladEventRepository>();
        services.AddScoped<IIronCladDeviceStateRepository, IronCladDeviceStateRepository>();

        // Background services
        services.AddHostedService<IronCladHeartbeatService>();
        services.AddHostedService<IronCladStateReconciliationService>();
    }
}

