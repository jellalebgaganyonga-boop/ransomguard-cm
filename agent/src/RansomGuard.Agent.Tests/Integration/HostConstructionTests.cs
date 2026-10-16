using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Service;
using Serilog;
using Shouldly;

namespace RansomGuard.Agent.Tests.Integration;

/// <summary>
/// Builds the agent's full service set — every registration of ServiceRegistration plus the
/// monitors — with scope validation switched on, so the next captive dependency fails here,
/// in CI, and not on a hospital workstation.
/// </summary>
/// <remarks>
/// .NET validates scopes only in the Development environment, and the agent runs in
/// Production: a singleton (every hosted service) capturing a scoped service (every DbContext
/// consumer) went unnoticed from Sprint 5 until a Development run (IronClad reconciliation).
/// The validation is deliberately NOT enabled in Production: a security agent that refuses to
/// start leaves a hospital unprotected. It bites in tests, not at the customer.
/// </remarks>
public sealed class HostConstructionTests
{
    [Fact]
    public void Full_agent_service_set_builds_with_scope_validation()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "RansomGuard.Agent.Service", "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Server:BaseUrl"] = "https://localhost",
                ["Agent:Database:ConnectionString"] = $"Data Source={Path.GetTempFileName()}",
                ["Agent:Logging:LogFilePath"] = Path.Combine(Path.GetTempPath(), "rg-host-test.log"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerilog(new LoggerConfiguration().CreateLogger()); // as Program.cs (AddSerilog)
        ServiceRegistration.ConfigureServices(services, config);
        ServiceRegistration.AddMonitors(services);

        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };

        Should.NotThrow(() => services.BuildServiceProvider(options).Dispose());
    }
}
