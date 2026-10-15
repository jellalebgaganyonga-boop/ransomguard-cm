using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// The Development fault injection used by the LOT 2 demo (scenario 4): refused outside
/// Development, empty in production, and in Development a real stop whose trace says it was
/// provoked.
/// </summary>
public sealed class ModuleFaultInjectionTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly string _triggers = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"rg_fault_{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        _h.Dispose();
        try { Directory.Delete(_triggers, recursive: true); } catch (IOException) { }
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Agent:Identity:Id"] = "auto-generated-on-first-run",
        ["Agent:Identity:Hostname"] = "AUTO",
        ["Agent:Detection:WatchPaths:0"] = @"C:\Temp",
        ["Agent:Server:BaseUrl"] = "https://localhost",
    };

    private static IConfiguration Config(string? faultInjection)
    {
        Dictionary<string, string?> settings = ValidSettings();
        if (faultInjection is not null)
        {
            settings[ModuleFaultInjectionPolicy.SettingKey] = faultInjection;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // ── Refused outside Development ────────────────────────────────────────────

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Production", "false")] // its mere presence: a shipped file must not carry it
    [InlineData("Staging", "true")]
    [InlineData(null, "true")]          // no environment counts as Production
    public void The_key_outside_Development_refuses_the_startup_and_names_it(string? environment, string value)
    {
        AgentConfigurationLoadResult result = AgentConfigurationLoader.Load(Config(value), environment);

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldContain(e => e.StartsWith(ModuleFaultInjectionPolicy.SettingKey) && e.Contains("refused"));
    }

    [Fact]
    public void In_Development_the_key_is_accepted_and_an_invalid_value_is_refused()
    {
        AgentConfigurationLoader.Load(Config("true"), "Development").Errors.ShouldBeEmpty();
        AgentConfigurationLoader.Load(Config("maybe"), "Development").Errors
            .ShouldContain(e => e.StartsWith(ModuleFaultInjectionPolicy.SettingKey));
    }

    // ── Empty in production ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    public void Anywhere_but_Development_with_true_the_injector_never_stops_anything(string environment, string? value)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleFaultInjector, NoModuleFaultInjector>(); // as ConfigureServices registers it
        services.AddLogging();

        ServiceRegistration.AddModuleFaultInjection(services, Config(value), environment).ShouldBeFalse();

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IModuleFaultInjector>().ShouldBeOfType<NoModuleFaultInjector>();
    }

    [Fact]
    public void Development_with_true_registers_the_development_injector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleFaultInjector, NoModuleFaultInjector>();
        services.AddLogging();

        ServiceRegistration.AddModuleFaultInjection(services, Config("true"), "Development").ShouldBeTrue();

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IModuleFaultInjector>().ShouldBeOfType<DevelopmentModuleFaultInjector>();
    }

    // ── In Development: the three traces, fault_injection: true ───────────────

    private EntropyMonitor Entropy(IModuleFailureReporter reporter, IModuleFaultInjector injector) => new(
        NullLogger<EntropyMonitor>.Instance,
        MonitorHarness.Config([_h.Folder], entropy: new EntropyOptions()),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        Mock.Of<IEntropyCalculator>(),
        _h.Registry,
        reporter,
        startupDelay: TimeSpan.Zero,
        faultInjector: injector);

    private async Task<List<string>> ModuleStoppedDetailsAsync()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AgentDbContext>().AuditLogs
            .Where(a => a.Action == ModuleFailureReporter.ModuleStoppedAction)
            .Select(a => a.Details).ToListAsync();
    }

    [Fact(Timeout = 30_000)]
    public async Task The_trigger_file_stops_ENTROPY_with_registry_critical_log_and_audit_marked_as_injected()
    {
        var reporterLog = new RecordingLogger<ModuleFailureReporter>();
        var reporter = new ModuleFailureReporter(_h.Registry, _h.ScopeFactory, reporterLog);
        var injector = new DevelopmentModuleFaultInjector(NullLogger<DevelopmentModuleFaultInjector>.Instance, _triggers);
        using EntropyMonitor monitor = Entropy(reporter, injector);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Entropy).State == ModuleState.Active, "ENTROPY active");
        string trigger = Path.Combine(_triggers, "ENTROPY.stop");
        await File.WriteAllTextAsync(trigger, "");

        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).ReasonCode == ModuleReasonCode.StoppedUnexpectedly, "ENTROPY stopped");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Entropy).State.ShouldBe(ModuleState.Inactive);
        reporterLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains("fault injection"));
        string details = (await ModuleStoppedDetailsAsync()).ShouldHaveSingleItem();
        JsonDocument.Parse(details).RootElement.GetProperty("fault_injection").GetBoolean().ShouldBeTrue();
        File.Exists(trigger).ShouldBeFalse(); // consumed: a restart does not stop it again
    }

    [Fact(Timeout = 30_000)]
    public async Task An_unknown_module_name_is_ignored_with_a_warning_and_consumed()
    {
        var injectorLog = new RecordingLogger<DevelopmentModuleFaultInjector>();
        var injector = new DevelopmentModuleFaultInjector(injectorLog, _triggers);
        using EntropyMonitor monitor = Entropy(_h.Reporter, injector);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Entropy).State == ModuleState.Active, "ENTROPY active");
        foreach (string name in new[] { "GENEALOGY.stop", "entropy.stop", "NOT_A_MODULE.stop" })
        {
            await File.WriteAllTextAsync(Path.Combine(_triggers, name), "");
        }

        await Eventually.HoldsAsync(() => Directory.GetFiles(_triggers).Length == 0, "the unknown files consumed");
        await Task.Delay(1_500); // three more scans
        await monitor.StopAsync(CancellationToken.None);

        _h.Registry.Published.ShouldNotContain(p => p.Code == ModuleCode.Entropy && p.State == ModuleState.Inactive);
        injectorLog.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("names no stoppable module")).ShouldBe(3);
    }

    [Fact]
    public async Task A_real_failure_is_recorded_with_fault_injection_false()
    {
        var reporter = new ModuleFailureReporter(_h.Registry, _h.ScopeFactory, NullLogger<ModuleFailureReporter>.Instance);

        await reporter.ReportStoppedAsync(ModuleCode.Entropy, reachedActive: true, new IOException("disk"));

        string details = (await ModuleStoppedDetailsAsync()).ShouldHaveSingleItem();
        JsonDocument.Parse(details).RootElement.GetProperty("fault_injection").GetBoolean().ShouldBeFalse();
    }
}
