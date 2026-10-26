using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Security.RateLimiting;
using RansomGuard.Agent.Service;
using Serilog;
using Shouldly;

namespace RansomGuard.Agent.Tests.Integration;

/// <summary>
/// V5 (plan de finition, J3 § 3.2): every consumer of file events has its own de-duplication
/// filter. With one filter shared by the Worker and ENTROPY, as the production registration had
/// it, the first watcher to see a write took the slot and the other one never got the event:
/// ENTROPY lost about half of the writes while both were "active".
/// </summary>
public sealed class FileEventDeduplicationPerConsumerTests : IDisposable
{
    private const int Writes = 20;

    private readonly string _dir = Directory.CreateTempSubdirectory("rg-dedup-").FullName;
    private readonly string _watched;
    private readonly CountingCalculator _calculator = new();

    public FileEventDeduplicationPerConsumerTests()
    {
        _watched = Directory.CreateDirectory(Path.Combine(_dir, "watched")).FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void Each_consumer_resolves_its_own_filter_from_the_production_registration()
    {
        using ServiceProvider provider = BuildProductionContainer();

        IFileEventDeduplicator forWorker = provider.GetRequiredService<IFileEventDeduplicator>();
        IFileEventDeduplicator forEntropy = provider.GetRequiredService<IFileEventDeduplicator>();

        ReferenceEquals(forWorker, forEntropy).ShouldBeFalse("one shared filter: the first consumer to see a write hides it from the others");
    }

    [Fact(Timeout = 60_000)]
    public async Task With_the_Worker_watching_the_same_folder_ENTROPY_sees_every_write()
    {
        using ServiceProvider provider = BuildProductionContainer();
        using Worker worker = ActivatorUtilities.CreateInstance<Worker>(provider);
        using EntropyMonitor entropy = Entropy(provider);
        await worker.StartAsync(CancellationToken.None);
        await entropy.StartAsync(CancellationToken.None);
        IModuleStateRegistry registry = provider.GetRequiredService<IModuleStateRegistry>();
        await WaitUntilAsync(() => registry.Snapshot().Single(e => e.Code == ModuleCode.Entropy).State == ModuleState.Degraded, "ENTROPY running (degraded: AGT-ATTR-001, AGT-EN-002)");
        await Task.Delay(500); // both watchers armed

        byte[] content = new byte[2048];
        Random.Shared.NextBytes(content);
        for (int i = 0; i < Writes; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(_watched, $"document_{i:00}.bin"), content);
            await Task.Delay(50);
        }

        await WaitUntilAsync(() => _calculator.Seen.Count >= Writes, $"ENTROPY analysing all {Writes} writes (seen {_calculator.Seen.Count})", 15);
        _calculator.Seen.Count.ShouldBe(Writes);

        await entropy.StopAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    private ServiceProvider BuildProductionContainer()
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
                ["Agent:Detection:WatchPaths:0"] = _watched,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerilog(new LoggerConfiguration().CreateLogger()); // as Program.cs (AddSerilog)
        ServiceRegistration.ConfigureServices(services, config);
        services.Replace(ServiceDescriptor.Singleton<IEntropyCalculator>(_calculator));
        services.Replace(ServiceDescriptor.Singleton(Mock.Of<IAlertForwardingQueue>(q => q.TryEnqueue(It.IsAny<Core.Communication.Models.AlertIngestRequest>()) == true)));

        ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.Migrate();

        // Migrated here rather than by DatabaseStartupService, because this test starts the
        // monitors by hand rather than running the host. The monitors wait for the schema before
        // they read anything, so whoever created it says it is there.
        provider.GetRequiredService<DatabaseReady>().Signal();
        return provider;
    }

    // ENTROPY as the host builds it, every dependency from the container -- the filter included --
    // with the startup delay removed.
    private static EntropyMonitor Entropy(IServiceProvider provider) => new(
        provider.GetRequiredService<ILogger<EntropyMonitor>>(),
        provider.GetRequiredService<IOptionsMonitor<AgentConfiguration>>(),
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IFileEventDeduplicator>(),
        provider.GetRequiredService<IEntropyCalculator>(),
        provider.GetRequiredService<IModuleStateRegistry>(),
        provider.GetRequiredService<IModuleFailureReporter>(),
        provider.GetService<RateLimiterFactory>(),
        provider.GetService<IDetectionEventBus>(),
        provider.GetService<IAlertForwardingQueue>(),
        startupDelay: TimeSpan.Zero,
        profiles: provider.GetService<UserProfileEnumerator>());

    private static async Task WaitUntilAsync(Func<bool> condition, string what, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"not reached within {seconds} s: {what}");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Records every file ENTROPY asks about; the value itself raises no alert.</summary>
    private sealed class CountingCalculator : IEntropyCalculator
    {
        public ConcurrentDictionary<string, byte> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<double?> ComputeFileEntropyAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Seen[filePath] = 1;
            return Task.FromResult<double?>(1.0);
        }

        public double ComputeEntropy(ReadOnlySpan<byte> data) => 1.0;
    }
}
