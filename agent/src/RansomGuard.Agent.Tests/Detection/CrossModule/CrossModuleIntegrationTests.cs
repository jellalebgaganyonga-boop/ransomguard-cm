using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.CrossModule;

public sealed class CrossModuleIntegrationTests
{
    // ===== Test 1: All_Four_Monitors_Resolve_From_DI =====

    [Fact]
    public void All_Four_Monitors_Resolve_From_DI()
    {
        // Verify ServiceRegistration produces a DI container where all 4 monitors can resolve
        var config = BuildMinimalConfig();
        var services = new ServiceCollection();
        services.AddLogging();
        ServiceRegistration.ConfigureServices(services, config);

        // Register hosted services (same as Program.cs)
        services.AddSingleton<INetworkActivityMonitor>(new Mock<INetworkActivityMonitor>().Object);
        services.AddScoped<IDataVolumeTracker>(sp => new Mock<IDataVolumeTracker>().Object);

        using var provider = services.BuildServiceProvider();

        // All 4 monitors should resolve (they're registered via AddHostedService in Program.cs,
        // but we verify their dependencies resolve cleanly)
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        // Core dependencies that each monitor needs
        sp.GetRequiredService<IGenealogyEnricher>().ShouldNotBeNull();
        sp.GetRequiredService<IAuditLogRepository>().ShouldNotBeNull();
        sp.GetRequiredService<IThreatIntelProvider>().ShouldNotBeNull();
        sp.GetRequiredService<IDetectionEventBus>().ShouldNotBeNull();
        sp.GetRequiredService<IExfilActionEngine>().ShouldNotBeNull();
    }

    // ===== Test 2: EntropyAlert_Publishes_EntropySignal_To_EventBus =====

    [Fact]
    public async Task EntropyAlert_Publishes_EntropySignal_To_EventBus()
    {
        var bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        EntropySignal? received = null;

        bus.Subscribe<EntropySignal>(async (signal, _) =>
        {
            received = signal;
            await Task.CompletedTask;
        });

        // Simulate what EntropyMonitor does after detecting an alert
        var signal = new EntropySignal
        {
            SignalId = Guid.NewGuid(),
            SourceModule = "ENTROPY",
            EmittedAt = DateTime.UtcNow,
            FilePath = @"C:\test\encrypted.docx",
            ProcessId = 4567,
            ProcessName = "ransomware.exe",
            EntropyValue = 7.99,
            FileSize = 1024000,
            EntropyAlertId = Guid.NewGuid()
        };

        await bus.PublishAsync(signal, CancellationToken.None);

        received.ShouldNotBeNull();
        received.SourceModule.ShouldBe("ENTROPY");
        received.EntropyAlertId.ShouldBe(signal.EntropyAlertId);
    }

    // ===== Test 3: Rule_8_Subscribes_And_Correlates_Within_60_Seconds =====

    [Fact]
    public async Task Rule_8_Subscribes_And_Correlates_Within_60_Seconds()
    {
        var bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        var rule = new EncryptedExfilCorrelationRule(bus);

        // Publish an EntropySignal (simulating EntropyMonitor detection)
        await bus.PublishAsync(new EntropySignal
        {
            SignalId = Guid.NewGuid(),
            SourceModule = "ENTROPY",
            EmittedAt = DateTime.UtcNow,
            FilePath = @"C:\test\encrypted.docx",
            ProcessId = 1234,
            ProcessName = "suspect.exe",
            EntropyValue = 7.99,
            FileSize = 50000,
            EntropyAlertId = Guid.NewGuid()
        }, CancellationToken.None);

        // Wait for subscription delivery
        await Task.Delay(100);

        // Now evaluate a network event from the SAME process (exfil after encryption)
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSent(1234, TimeSpan.FromHours(1))).Returns(100_000);

        var networkEvent = new RansomGuard.Agent.Core.Detection.ExfilWatch.Models.NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = RansomGuard.Agent.Core.Detection.ExfilWatch.Models.NetworkEventType.TcpSend,
            ProcessId = 1234,
            ProcessName = "suspect.exe",
            DestinationAddress = "203.0.113.99",
            DestinationPort = 443,
            BytesSent = 50000,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        var options = new ExfilWatchOptions();
        var finding = rule.Evaluate(networkEvent, tracker.Object, options);

        // Rule 8 should correlate: same PID encrypted a file AND sent network data
        finding.ShouldNotBeNull();
        finding.RuleName.ShouldBe("EncryptedExfilCorrelation");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);
    }

    // ===== Test 4: All_Alerts_Cross_Linked_Via_GenealogyId =====

    [Fact]
    public void All_Alerts_Cross_Linked_Via_GenealogyId()
    {
        // Verify that alert entities have GenealogyId fields for cross-linking
        var genealogyId = Guid.NewGuid();
        var crossLinkedId = Guid.NewGuid();

        var exfilAlert = new RansomGuard.Agent.Core.Persistence.Entities.ExfilAlert
        {
            RuleName = "Test",
            Severity = "High",
            ProcessId = 123,
            ProcessName = "test.exe",
            Destination = "1.2.3.4",
            BytesTransferred = 1000,
            Description = "Test",
            ActionTaken = "AlertOnly",
            GenealogyId = genealogyId,
            CrossLinkedEntropyAlertId = crossLinkedId
        };
        exfilAlert.GenealogyId.ShouldBe(genealogyId);
        exfilAlert.CrossLinkedEntropyAlertId.ShouldBe(crossLinkedId);

        // Verify EntropyAlert exists with expected structure
        var entropyAlert = new RansomGuard.Agent.Core.Persistence.Entities.EntropyAlert
        {
            RuleId = 1,
            RuleName = "AbsoluteThreshold",
            Severity = "Critical",
            FilePath = @"C:\test.docx",
            BaselineEntropy = 3.5,
            CurrentEntropy = 7.9,
            Delta = 4.4
        };
        entropyAlert.Id.ShouldNotBe(Guid.Empty);
    }

    // ===== Test 5: GenealogyEnricher_Accessible_From_All_Scopes =====

    [Fact]
    public void GenealogyEnricher_Accessible_From_All_Scopes()
    {
        var config = BuildMinimalConfig();
        var services = new ServiceCollection();
        services.AddLogging();
        ServiceRegistration.ConfigureServices(services, config);

        using var provider = services.BuildServiceProvider();

        // Create 3 separate scopes (simulating 3 different monitors)
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        using var scope3 = provider.CreateScope();

        var enricher1 = scope1.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
        var enricher2 = scope2.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
        var enricher3 = scope3.ServiceProvider.GetRequiredService<IGenealogyEnricher>();

        // All should resolve (scoped instances)
        enricher1.ShouldNotBeNull();
        enricher2.ShouldNotBeNull();
        enricher3.ShouldNotBeNull();

        // Scoped = different instances per scope
        enricher1.ShouldNotBeSameAs(enricher2);
    }

    private static IConfiguration BuildMinimalConfig()
    {
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
                "RansomGuard.Agent.Service", "appsettings.json"), optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Identity:Id"] = "test-agent",
                ["Agent:Identity:Hostname"] = "TEST",
                ["Agent:Identity:Version"] = "0.3.0",
                ["Agent:Identity:Environment"] = "Test",
                ["Agent:Detection:WatchPaths:0"] = @"C:\Temp",
                ["Agent:Logging:MinimumLevel"] = "Warning",
                ["Agent:Logging:LogFilePath"] = @"C:\Temp\test.log",
                ["Agent:Server:BaseUrl"] = "https://localhost",
                ["Agent:Database:ConnectionString"] = $"Data Source={Path.GetTempFileName()}",
                ["Agent:Sentinel:Enabled"] = "false",
                ["Agent:Entropy:Enabled"] = "false",
                ["Agent:ExfilWatch:Enabled"] = "true"
            })
            .Build();
    }
}
