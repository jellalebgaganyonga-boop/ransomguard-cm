using Microsoft.Extensions.Configuration;
using RansomGuard.Agent.Core.Configuration;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// Tests deserialization of <see cref="AgentConfiguration"/> from JSON configuration.
/// </summary>
public sealed class AgentConfigurationTests
{
    [Fact]
    public void Should_deserialize_from_valid_json()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Agent:Identity:Id"] = "auto-generated-on-first-run",
            ["Agent:Identity:Hostname"] = "AUTO",
            ["Agent:Identity:Version"] = "0.3.0",
            ["Agent:Identity:Environment"] = "Development",
            ["Agent:Detection:WatchPaths:0"] = @"C:\Test",
            ["Agent:Detection:EnableFileSystemWatcher"] = "true",
            ["Agent:Detection:EnableETW"] = "false",
            ["Agent:Logging:MinimumLevel"] = "Information",
            ["Agent:Logging:LogFilePath"] = @"C:\logs\agent.log",
            ["Agent:Logging:MaxFileSizeMB"] = "50",
            ["Agent:Logging:RetainedFileCount"] = "30",
            ["Agent:Server:BaseUrl"] = "https://localhost:5001",
            ["Agent:Server:HeartbeatIntervalSeconds"] = "30",
            ["Agent:Server:ConnectionTimeoutSeconds"] = "10",
            ["Agent:Database:ConnectionString"] = "Data Source=agent.db",
            ["Agent:Database:MaxRetentionDays"] = "90",
        });

        config.ShouldNotBeNull();
        config.Identity.Id.ShouldBe("auto-generated-on-first-run");
        config.Identity.Hostname.ShouldBe("AUTO");
        config.Identity.Version.ShouldBe("0.3.0");
        config.Identity.Environment.ShouldBe("Development");
        config.Detection.WatchPaths.Length.ShouldBe(1);
        config.Detection.WatchPaths[0].ShouldBe(@"C:\Test");
        config.Detection.EnableFileSystemWatcher.ShouldBeTrue();
        config.Detection.EnableETW.ShouldBeFalse();
        config.Logging.MinimumLevel.ShouldBe("Information");
        config.Logging.MaxFileSizeMB.ShouldBe(50);
        config.Logging.RetainedFileCount.ShouldBe(30);
        config.Server.BaseUrl.ShouldBe("https://localhost:5001");
        config.Server.HeartbeatIntervalSeconds.ShouldBe(30);
        config.Server.ConnectionTimeoutSeconds.ShouldBe(10);
        config.Database.ConnectionString.ShouldBe("Data Source=agent.db");
        config.Database.MaxRetentionDays.ShouldBe(90);
    }

    [Fact]
    public void Should_deserialize_multiple_watch_paths()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Agent:Identity:Id"] = "auto-generated-on-first-run",
            ["Agent:Identity:Hostname"] = "AUTO",
            ["Agent:Identity:Version"] = "0.3.0",
            ["Agent:Identity:Environment"] = "Development",
            ["Agent:Detection:WatchPaths:0"] = @"C:\Path1",
            ["Agent:Detection:WatchPaths:1"] = @"C:\Path2",
            ["Agent:Detection:WatchPaths:2"] = @"C:\Path3",
            ["Agent:Detection:EnableFileSystemWatcher"] = "true",
            ["Agent:Logging:MinimumLevel"] = "Information",
            ["Agent:Logging:LogFilePath"] = @"C:\logs\agent.log",
            ["Agent:Server:BaseUrl"] = "https://localhost:5001",
            ["Agent:Database:ConnectionString"] = "Data Source=agent.db",
        });

        config.Detection.WatchPaths.Length.ShouldBe(3);
        config.Detection.WatchPaths.ShouldContain(@"C:\Path2");
    }

    [Fact]
    public void Should_use_default_values_for_optional_fields()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Agent:Identity:Id"] = "auto-generated-on-first-run",
            ["Agent:Identity:Hostname"] = "AUTO",
            ["Agent:Identity:Version"] = "0.3.0",
            ["Agent:Identity:Environment"] = "Development",
            ["Agent:Detection:WatchPaths:0"] = @"C:\Test",
            ["Agent:Logging:MinimumLevel"] = "Information",
            ["Agent:Logging:LogFilePath"] = @"C:\logs\agent.log",
            ["Agent:Server:BaseUrl"] = "https://localhost:5001",
            ["Agent:Database:ConnectionString"] = "Data Source=agent.db",
        });

        // Record defaults are preserved: EnableFileSystemWatcher defaults to true
        config.Detection.EnableFileSystemWatcher.ShouldBeTrue();
        config.Detection.EnableETW.ShouldBeFalse();
        // Int defaults from record are preserved via Bind on pre-constructed object
        config.Logging.MaxFileSizeMB.ShouldBe(50);
        config.Server.HeartbeatIntervalSeconds.ShouldBe(30);
        config.Database.MaxRetentionDays.ShouldBe(90);
    }

    private static AgentConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions
            {
                Id = "",
                Hostname = "",
                Version = "",
                Environment = ""
            },
            Detection = new DetectionOptions { WatchPaths = [] },
            Logging = new LoggingOptions { MinimumLevel = "", LogFilePath = "" },
            Server = new ServerOptions { BaseUrl = "" },
            Database = new DatabaseOptions { ConnectionString = "" }
        };

        configuration.GetSection("Agent").Bind(config);

        return config;
    }
}
