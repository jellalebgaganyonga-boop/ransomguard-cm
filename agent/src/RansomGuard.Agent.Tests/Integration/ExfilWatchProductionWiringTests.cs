using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Service;
using Serilog;
using Shouldly;

namespace RansomGuard.Agent.Tests.Integration;

/// <summary>
/// EXFIL-REG-001 (LOT VÉRITÉ, V1): EXFIL WATCH built by the REAL ServiceRegistration -- not a test
/// container -- must turn a network event into a finding, then into its response. Before the fix
/// it published "active" while IDataVolumeTracker was registered nowhere in production: the
/// consumer resolved it to null and skipped every rule (ExfilWatchMonitor.ConsumeEventsAsync).
///
/// Only what touches the machine is replaced: the ETW capture (a fake that feeds the channel), the
/// firewall and the throttler (a Critical finding blocks an IP and slows a process), and the GRID
/// queue (captured). Database, audit, rules, threat intel and action engine are the production ones.
/// </summary>
public sealed class ExfilWatchProductionWiringTests : IDisposable
{
    private const string Destination = "203.0.113.50"; // TEST-NET-3, never routed

    private readonly string _dir = Directory.CreateTempSubdirectory("rg-exfil-wiring-").FullName;
    private readonly List<AlertIngestRequest> _alerts = [];
    private readonly FakeCapture _capture = new();
    private readonly Mock<IFirewallManager> _firewall = new();

    public ExfilWatchProductionWiringTests()
    {
        _firewall.Setup(f => f.AddBlockRule(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort: SQLite may still hold the file for a moment
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task A_LOLBAS_upload_becomes_an_alert_through_the_production_registrations()
    {
        using ServiceProvider provider = BuildProductionContainer();
        using ExfilWatchMonitor monitor = await StartedMonitorAsync(provider);

        await _capture.Writer!.WriteAsync(CertutilUpload());

        await WaitUntilAsync(
            () => { lock (_alerts) return _alerts.Any(a => a.AlertType == "ExfilWatchLolbasExfil"); },
            "the LolbasExfil finding forwarded to the GRID");
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_critical_finding_reaches_the_firewall_through_the_production_registrations()
    {
        using ServiceProvider provider = BuildProductionContainer();
        using ExfilWatchMonitor monitor = await StartedMonitorAsync(provider);

        await _capture.Writer!.WriteAsync(CertutilUpload());

        // LolbasExfil is Critical: alert, throttle, then block the destination.
        await WaitUntilAsync(
            () => _firewall.Invocations.Any(i =>
                i.Method.Name == nameof(IFirewallManager.AddBlockRule) && (string)i.Arguments[1] == Destination),
            $"a firewall block rule for {Destination}");
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task During_the_learning_phase_the_module_reads_degraded_learning_phase_not_active()
    {
        // The shipped configuration: 7 days of learning from the tracker's creation.
        using ServiceProvider provider = BuildProductionContainer();
        using ExfilWatchMonitor monitor = await StartedMonitorAsync(provider);

        ModuleStateEntry state = provider.GetRequiredService<IModuleStateRegistry>().Snapshot().Single(e => e.Code == ModuleCode.ExfilWatch);
        state.State.ShouldBe(ModuleState.Degraded);
        state.ReasonCode.ShouldBe(ModuleReasonCode.LearningPhase);
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task Past_the_learning_phase_the_module_reads_active()
    {
        using ServiceProvider provider = BuildProductionContainer(services =>
            services.Replace(ServiceDescriptor.Singleton<IDataVolumeTracker>(
                new DataVolumeTracker(NullLogger<DataVolumeTracker>.Instance, learningPhaseDays: 0))));
        using ExfilWatchMonitor monitor = await StartedMonitorAsync(provider);

        provider.GetRequiredService<IModuleStateRegistry>().Snapshot().Single(e => e.Code == ModuleCode.ExfilWatch)
            .State.ShouldBe(ModuleState.Active);
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_chain_with_a_missing_service_never_reads_active_it_reads_inactive_init_failed()
    {
        // The registration gone again (EXFIL-REG-001): the module must say so, not pretend.
        using ServiceProvider provider = BuildProductionContainer(services => services.RemoveAll<IDataVolumeTracker>());
        var monitor = ActivatorUtilities.CreateInstance<ExfilWatchMonitor>(provider, (Func<bool>)(() => true));
        await monitor.StartAsync(CancellationToken.None);
        IModuleStateRegistry registry = provider.GetRequiredService<IModuleStateRegistry>();

        await WaitUntilAsync(
            () => registry.Snapshot().Single(e => e.Code == ModuleCode.ExfilWatch).ReasonCode == ModuleReasonCode.InitFailed,
            "EXFIL WATCH publishes init_failed");
        registry.Snapshot().Single(e => e.Code == ModuleCode.ExfilWatch).State.ShouldBe(ModuleState.Inactive);
        _capture.Writer.ShouldBeNull(); // the capture never started: nothing runs behind a failed start
        await monitor.StopAsync(CancellationToken.None);
        monitor.Dispose();
    }

    /// <summary>certutil.exe sending 2 MB: LolbasExfil, a rule that does not wait for the learning phase.</summary>
    private static NetworkEvent CertutilUpload() => new()
    {
        Id = Guid.NewGuid(),
        EventType = NetworkEventType.TcpSend,
        ProcessId = 4242,
        ProcessName = "certutil.exe",
        DestinationAddress = Destination,
        DestinationPort = 443,
        BytesSent = 2_000_000,
        Protocol = "TCP",
        Timestamp = DateTime.UtcNow,
    };

    private ServiceProvider BuildProductionContainer(Action<IServiceCollection>? adjust = null)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "RansomGuard.Agent.Service", "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Server:BaseUrl"] = "https://localhost",
                ["Agent:Database:ConnectionString"] = $"Data Source={Path.Combine(_dir, "agent.db")}",
                ["Agent:Database:KeyDirectory"] = Path.Combine(_dir, "keys"),
                ["Agent:Logging:LogFilePath"] = Path.Combine(_dir, "agent.log"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerilog(new LoggerConfiguration().CreateLogger()); // as Program.cs (AddSerilog)
        ServiceRegistration.ConfigureServices(services, config);

        services.Replace(ServiceDescriptor.Singleton<INetworkActivityMonitor>(_capture));
        services.Replace(ServiceDescriptor.Singleton(_firewall.Object));
        services.Replace(ServiceDescriptor.Singleton(Mock.Of<IProcessThrottler>(t =>
            t.ApplyThrottle(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>()) == true)));
        var queue = new Mock<IAlertForwardingQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<AlertIngestRequest>()))
            .Callback((AlertIngestRequest a) => { lock (_alerts) _alerts.Add(a); })
            .Returns(true);
        services.Replace(ServiceDescriptor.Singleton(queue.Object));
        adjust?.Invoke(services);

        ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.Migrate();
        return provider;
    }

    private async Task<ExfilWatchMonitor> StartedMonitorAsync(IServiceProvider provider)
    {
        // Elevated, as the installed service: otherwise the module stops at admin_rights_missing.
        var monitor = ActivatorUtilities.CreateInstance<ExfilWatchMonitor>(provider, (Func<bool>)(() => true));
        await monitor.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _capture.Writer is not null, "the network capture started");
        return monitor;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"not reached within 10 s: {what}");
            await Task.Delay(20);
        }
    }

    /// <summary>The kernel ETW network capture, replaced: the test writes the events.</summary>
    private sealed class FakeCapture : INetworkActivityMonitor
    {
        public ChannelWriter<NetworkEvent>? Writer { get; private set; }

        public bool IsCapturing => Writer is not null;

        public long TotalEventsCaptured => 0;

        public void Start(ChannelWriter<NetworkEvent> writer, CancellationToken cancellationToken) => Writer = writer;

        public Task StopAsync() => Task.CompletedTask;
    }
}
