using FluentValidation.Results;
using RansomGuard.Agent.Core.Configuration;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for SENTINEL configuration validation.
/// </summary>
public sealed class SentinelConfigurationTests
{
    private readonly AgentConfigurationValidator _validator = new();

    [Fact]
    public void Valid_sentinel_config_should_pass()
    {
        AgentConfiguration config = CreateConfigWithSentinel();
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Config_without_sentinel_should_pass()
    {
        AgentConfiguration config = CreateConfigWithSentinel() with { Sentinel = null };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Out_of_range_canaries_per_directory_should_fail(int count)
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with { CanariesPerDirectory = count }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(99)]
    [InlineData(10001)]
    public void Out_of_range_check_interval_should_fail(int ms)
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with { CheckIntervalMs = ms }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Empty_watch_directories_should_fail()
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with { WatchDirectories = [] }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Fewer_than_3_templates_should_fail()
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with { CanaryTemplates = ["a", "b"] }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Empty_canary_prefix_should_fail()
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with { CanaryPrefix = "" }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Watch_directory_with_env_variable_should_pass()
    {
        AgentConfiguration config = CreateConfigWithSentinel() with
        {
            Sentinel = CreateConfigWithSentinel().Sentinel! with
            {
                WatchDirectories = ["%USERPROFILE%\\Desktop"]
            }
        };
        ValidationResult result = _validator.Validate(config);
        result.IsValid.ShouldBeTrue();
    }

    private static AgentConfiguration CreateConfigWithSentinel() => new()
    {
        Identity = new AgentIdentityOptions
        {
            Id = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            Hostname = "TEST-HOST",
            Version = "0.4.0",
            Environment = "Development"
        },
        Detection = new DetectionOptions
        {
            WatchPaths = [@"C:\Test"],
            EnableFileSystemWatcher = true
        },
        Logging = new LoggingOptions
        {
            MinimumLevel = "Information",
            LogFilePath = @"C:\Logs\agent.log",
            MaxFileSizeMB = 50,
            RetainedFileCount = 30
        },
        Server = new ServerOptions
        {
            BaseUrl = "https://localhost:5001",
            HeartbeatIntervalSeconds = 30,
            ConnectionTimeoutSeconds = 10
        },
        Database = new DatabaseOptions
        {
            ConnectionString = "Data Source=agent.db",
            MaxRetentionDays = 90
        },
        Sentinel = new SentinelOptions
        {
            Enabled = true,
            CanariesPerDirectory = 3,
            CheckIntervalMs = 1000,
            WatchDirectories = [@"C:\Test\Sentinel"],
            CanaryTemplates = ["dossier_patient", "analyses_laboratoire", "imagerie_medicale"],
            CanaryPrefix = "0001_"
        }
    };
}
