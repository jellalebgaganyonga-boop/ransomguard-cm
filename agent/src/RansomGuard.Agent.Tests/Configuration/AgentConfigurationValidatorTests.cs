using FluentAssertions;
using FluentValidation.Results;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// Tests all validation rules for <see cref="AgentConfiguration"/>.
/// </summary>
public sealed class AgentConfigurationValidatorTests
{
    private readonly AgentConfigurationValidator _validator = new();

    [Fact]
    public void Valid_configuration_should_pass_validation()
    {
        AgentConfiguration config = CreateValidConfig();

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public void Invalid_agent_id_should_fail(string id)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Identity = CreateValidConfig().Identity with { Id = id }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Id"));
    }

    [Fact]
    public void Auto_generated_marker_should_pass()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Identity = CreateValidConfig().Identity with { Id = "auto-generated-on-first-run" }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_guid_should_pass()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Identity = CreateValidConfig().Identity with { Id = "a1b2c3d4-e5f6-7890-abcd-ef1234567890" }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("InvalidEnv")]
    [InlineData("dev")]
    public void Invalid_environment_should_fail(string environment)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Identity = CreateValidConfig().Identity with { Environment = environment }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Valid_environment_should_pass(string environment)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Identity = CreateValidConfig().Identity with { Environment = environment }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_watch_paths_should_fail()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Detection = CreateValidConfig().Detection with { WatchPaths = [] }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("WatchPaths"));
    }

    [Fact]
    public void Watch_path_with_environment_variable_should_pass()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Detection = CreateValidConfig().Detection with
            {
                WatchPaths = ["%USERPROFILE%\\Desktop\\Test"]
            }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_watch_path_entry_should_fail(string path)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Detection = CreateValidConfig().Detection with
            {
                WatchPaths = [path]
            }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("ftp://localhost")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Invalid_server_base_url_should_fail(string url)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Server = CreateValidConfig().Server with { BaseUrl = url }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_https_url_should_pass()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Server = CreateValidConfig().Server with { BaseUrl = "https://ransomguard.local:5001" }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(9)]
    [InlineData(3601)]
    public void Out_of_range_heartbeat_should_fail(int seconds)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Server = CreateValidConfig().Server with { HeartbeatIntervalSeconds = seconds }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(3600)]
    public void Valid_heartbeat_should_pass(int seconds)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Server = CreateValidConfig().Server with { HeartbeatIntervalSeconds = seconds }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_connection_string_should_fail()
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Database = CreateValidConfig().Database with { ConnectionString = "" }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Out_of_range_retention_days_should_fail(int days)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Database = CreateValidConfig().Database with { MaxRetentionDays = days }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("InvalidLevel")]
    public void Invalid_logging_level_should_fail(string level)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Logging = CreateValidConfig().Logging with { MinimumLevel = level }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Out_of_range_max_file_size_should_fail(int size)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Logging = CreateValidConfig().Logging with { MaxFileSizeMB = size }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Out_of_range_connection_timeout_should_fail(int seconds)
    {
        AgentConfiguration config = CreateValidConfig() with
        {
            Server = CreateValidConfig().Server with { ConnectionTimeoutSeconds = seconds }
        };

        ValidationResult result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
    }

    private static AgentConfiguration CreateValidConfig() => new()
    {
        Identity = new AgentIdentityOptions
        {
            Id = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            Hostname = "TEST-HOST",
            Version = "0.3.0",
            Environment = "Development"
        },
        Detection = new DetectionOptions
        {
            WatchPaths = [@"C:\Test\WatchPath"],
            EnableFileSystemWatcher = true,
            EnableETW = false
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
        }
    };
}
