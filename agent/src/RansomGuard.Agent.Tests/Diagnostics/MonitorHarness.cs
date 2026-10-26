using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Service;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// What a real monitor needs, with a real database: a shared in-memory SQLite, the real
/// repositories and canary service, and a switch that makes SENTINEL's periodic integrity
/// check fail on demand (a locked database, an I/O error).
/// </summary>
internal sealed class MonitorHarness : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    /// <param name="chainInBackground">
    /// True: the production audit pipeline (AGT-AUD-002) — record, then the background chain
    /// writer. False: the repository chains before returning, as tools do.
    /// </param>
    /// <param name="ownWrites">
    /// The set the canary service marks its own writes in, shared with the monitor as production
    /// shares it. Left null by default on purpose: creating a canary marks it for
    /// <see cref="AgentOwnWrites.DeploymentGrace"/>, a full minute, so a test that creates a decoy
    /// and then writes to it would have its own write forgiven as the agent's. A test that wants
    /// the restore path has to pass one, and clear the deployment mark once the decoy is planted.
    /// </param>
    public MonitorHarness(bool chainInBackground = false, AgentOwnWrites? ownWrites = null)
    {
        string db = $"file:monitor_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection($"Data Source={db}");
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite($"Data Source={db}"));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<ISentinelCanaryRepository, SentinelCanaryRepository>();
        services.AddSingleton(Health);
        if (chainInBackground)
        {
            services.AddSingleton<AuditChainWriter>();
            services.AddSingleton<IAuditChainWriter>(sp => sp.GetRequiredService<AuditChainWriter>());
            services.AddScoped<IAuditLogRepository>(sp => new AuditLogRepository(
                sp.GetRequiredService<AgentDbContext>(), null, sp.GetRequiredService<IAuditChainWriter>(), Health,
                AuditLog));
        }
        else
        {
            services.AddScoped<IAuditLogRepository>(sp => new AuditLogRepository(sp.GetRequiredService<AgentDbContext>()));
        }
        if (ownWrites is not null)
        {
            services.AddSingleton(ownWrites);
        }
        services.AddScoped<CanaryFileService>();
        services.AddSingleton(IntegrityCheck);
        services.AddScoped<ICanaryFileService, SwitchableCanaryFileService>();
        services.AddScoped(_ => Mock.Of<IGenealogyEnricher>());
        Provider = services.BuildServiceProvider();

        using IServiceScope scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();

        var queue = new Mock<IAlertForwardingQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<AlertIngestRequest>()))
            .Callback((AlertIngestRequest a) => { lock (Forwarded) Forwarded.Add(a); })
            .Returns(true);
        queue.Setup(q => q.TryEnqueueAttribution(It.IsAny<string>(), It.IsAny<AttributionComplementRequest>()))
            .Callback((string id, AttributionComplementRequest c) => { lock (Attributions) Attributions.Add((id, c)); })
            .Returns(true);
        AlertQueue = queue.Object;

        Reporter = new ModuleFailureReporter(Registry, ScopeFactory, NullLogger<ModuleFailureReporter>.Instance);
        Folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rg_monitor_{Guid.NewGuid():N}")).FullName;
    }

    public ServiceProvider Provider { get; }

    /// <summary>Attribution complements enqueued (ADR-025), with their alert's client_message_id.</summary>
    public List<(string ClientMessageId, AttributionComplementRequest Complement)> Attributions { get; } = [];

    public AuditHealth Health { get; } = new();

    public RecordingLogger<AuditLogRepository> AuditLog { get; } = new();

    /// <summary>The background chain writer (only with chainInBackground).</summary>
    public AuditChainWriter ChainWriter => Provider.GetRequiredService<AuditChainWriter>();

    /// <summary>Inserts entries straight into the waiting table, in one transaction: a saturated audit.</summary>
    public void SaturateAudit(int entries)
    {
        using IServiceScope scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        DateTime now = DateTime.UtcNow;
        db.PendingAuditEntries.AddRange(Enumerable.Range(0, entries).Select(i => new PendingAuditEntry
        {
            Action = "Burst",
            Details = $"{{\"n\":{i}}}",
            CreatedAt = now,
        }));
        db.SaveChanges();
    }

    public int Waiting()
    {
        using IServiceScope scope = Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AgentDbContext>().PendingAuditEntries.Count();
    }

    public List<string> ChainedActions()
    {
        using IServiceScope scope = Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AgentDbContext>().AuditLogs
            .OrderBy(a => a.Sequence).Select(a => a.Action).ToList();
    }

    public async Task<bool> ChainIntactAsync()
    {
        using IServiceScope scope = Provider.CreateScope();
        return await new AuditLogRepository(scope.ServiceProvider.GetRequiredService<AgentDbContext>())
            .VerifyChainIntegrityAsync();
    }

    public IServiceScopeFactory ScopeFactory => Provider.GetRequiredService<IServiceScopeFactory>();

    public RecordingRegistry Registry { get; } = new();

    public IntegrityCheckSwitch IntegrityCheck { get; } = new();

    public IModuleFailureReporter Reporter { get; }

    public IAlertForwardingQueue AlertQueue { get; }

    public List<AlertIngestRequest> Forwarded { get; } = [];

    /// <summary>An existing folder, removed at the end.</summary>
    public string Folder { get; }

    public static string MissingFolder() => Path.Combine(Path.GetTempPath(), $"rg_missing_{Guid.NewGuid():N}");

    public static IOptionsMonitor<AgentConfiguration> Config(
        string[] watchPaths, SentinelOptions? sentinel = null, EntropyOptions? entropy = null,
        UsbGuardOptions? usbGuard = null)
    {
        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions { Id = "test-agent", Hostname = "TEST" },
            Server = new ServerOptions { BaseUrl = "https://localhost" },
            Detection = new DetectionOptions { WatchPaths = watchPaths },
            Database = new DatabaseOptions { ConnectionString = "Data Source=:memory:" },
            Logging = new LoggingOptions { MinimumLevel = "Warning", LogFilePath = "test.log" },
            Sentinel = sentinel,
            Entropy = entropy,
            UsbGuard = usbGuard,
        };
        var monitor = new Mock<IOptionsMonitor<AgentConfiguration>>();
        monitor.Setup(m => m.CurrentValue).Returns(config);
        return monitor.Object;
    }

    public async Task<SentinelCanary> CreateCanaryAsync(string folder)
    {
        using IServiceScope scope = Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CanaryFileService>()
            .CreateCanaryAsync(folder, "dossier_patient", "0001_");
    }

    public ModuleStateEntry State(string code) => Registry.Snapshot().Single(e => e.Code == code);

    public void Dispose()
    {
        Provider.Dispose();
        _keepAlive.Dispose();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still hold the folder for a moment; it is in %TEMP%.
        }
    }
}

/// <summary>Makes GetAllCanariesAsync — the start of every periodic check — fail on demand.</summary>
internal sealed class IntegrityCheckSwitch
{
    private int _calls;
    private volatile bool _failing;

    public bool Failing
    {
        get => _failing;
        set => _failing = value;
    }

    public int Calls => Volatile.Read(ref _calls);

    public void Called() => Interlocked.Increment(ref _calls);
}

internal sealed class SwitchableCanaryFileService(CanaryFileService inner, IntegrityCheckSwitch check) : ICanaryFileService
{
    public Task<SentinelCanary> CreateCanaryAsync(string directory, string template, string prefix,
        CancellationToken cancellationToken = default) =>
        inner.CreateCanaryAsync(directory, template, prefix, cancellationToken);

    public Task<bool> VerifyCanaryIntegrityAsync(SentinelCanary canary, CancellationToken cancellationToken = default) =>
        inner.VerifyCanaryIntegrityAsync(canary, cancellationToken);

    public Task DeleteCanaryAsync(SentinelCanary canary, CancellationToken cancellationToken = default) =>
        inner.DeleteCanaryAsync(canary, cancellationToken);

    public Task RestoreCanaryAsync(SentinelCanary canary, CancellationToken cancellationToken = default) =>
        inner.RestoreCanaryAsync(canary, cancellationToken);

    public Task<IReadOnlyList<SentinelCanary>> GetAllCanariesAsync(CancellationToken cancellationToken = default)
    {
        check.Called();
        return check.Failing
            ? throw new IOException("database is locked (test)")
            : inner.GetAllCanariesAsync(cancellationToken);
    }
}

/// <summary>The real registry, plus every transition published to it, in order.</summary>
internal sealed class RecordingRegistry : IModuleStateRegistry
{
    private readonly InMemoryModuleStateRegistry _inner = new();
    private readonly List<(string Code, ModuleState State, string? Reason)> _published = [];

    public IReadOnlyList<(string Code, ModuleState State, string? Reason)> Published
    {
        get { lock (_published) return _published.ToList(); }
    }

    public void Publish(string code, ModuleState state, string? reasonCode = null)
    {
        lock (_published) _published.Add((code, state, reasonCode));
        _inner.Publish(code, state, reasonCode);
    }

    public IReadOnlyList<ModuleStateEntry> Snapshot() => _inner.Snapshot();
}
