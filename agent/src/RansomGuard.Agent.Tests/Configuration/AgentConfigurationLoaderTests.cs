using Microsoft.Extensions.Configuration;
using RansomGuard.Agent.Core.Configuration;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// AgentConfigurationLoader: the configuration is validated once, before anything consumes it,
/// and a refusal names the exact key.
/// </summary>
public sealed class AgentConfigurationLoaderTests
{
    private static string ServiceDir => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "RansomGuard.Agent.Service");

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Agent:Identity:Id"] = "auto-generated-on-first-run",
        ["Agent:Identity:Hostname"] = "AUTO",
        ["Agent:Identity:Version"] = "0.3.0",
        ["Agent:Detection:WatchPaths:0"] = @"C:\Temp",
        ["Agent:Server:BaseUrl"] = "https://grid.example.org",
    };

    private static AgentConfigurationLoadResult Load(Dictionary<string, string?> settings, string environment = "Production") =>
        AgentConfigurationLoader.Load(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), environment);

    private static Dictionary<string, string?> With(string key, string? value)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings[key] = value;
        return settings;
    }

    // ── The shipped files ──────────────────────────────────────────

    [Fact]
    public void Shipped_appsettings_alone_is_refused_naming_the_missing_server_address()
    {
        IConfiguration shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.json"), optional: false)
            .Build();

        AgentConfigurationLoadResult result = AgentConfigurationLoader.Load(shipped, "Production");

        result.Configuration.ShouldBeNull();
        // One message, not two: BaseUrl validation stops at the first failure.
        result.Errors.ShouldHaveSingleItem().ShouldStartWith("Agent:Server:BaseUrl: is required and missing");
    }

    [Fact]
    public void Shipped_appsettings_with_the_Development_file_is_accepted_in_Development()
    {
        IConfiguration development = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.Development.json"), optional: false)
            .Build();

        AgentConfigurationLoadResult result = AgentConfigurationLoader.Load(development, "Development");

        result.Errors.ShouldBeEmpty();
        AgentConfiguration config = result.Configuration.ShouldNotBeNull();
        config.Server.BaseUrl.ShouldBe("https://localhost");
    }

    // ── One source, absolute defaults ──────────────────────────────

    [Fact]
    public void Valid_settings_load_with_absolute_defaults_for_logs_database_and_keys()
    {
        AgentConfigurationLoadResult result = Load(ValidSettings());

        result.Errors.ShouldBeEmpty();
        AgentConfiguration config = result.Configuration.ShouldNotBeNull();
        config.Logging.LogFilePath.ShouldBe(LoggingOptions.DefaultLogFilePath);
        config.Database.ConnectionString.ShouldBe(DatabaseOptions.DefaultConnectionString);
        config.Database.KeyDirectory.ShouldBe(DatabaseOptions.DefaultKeyDirectory);
        ConfigurationValueRules.IsAbsoluteAfterExpansion(config.Logging.LogFilePath).ShouldBeTrue();
        ConfigurationValueRules.IsAbsoluteAfterExpansion(
            ConfigurationValueRules.DataSource(config.Database.ConnectionString)).ShouldBeTrue();
        ConfigurationValueRules.IsAbsoluteAfterExpansion(config.Database.KeyDirectory).ShouldBeTrue();
    }

    [Fact]
    public void Missing_Agent_section_is_refused()
    {
        AgentConfigurationLoadResult result = Load(new Dictionary<string, string?>());

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldContain(e => e.StartsWith("Agent:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unconvertible_value_is_refused_not_thrown()
    {
        AgentConfigurationLoadResult result = Load(With("Agent:Server:HeartbeatIntervalSeconds", "abc"));

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldNotBeEmpty();
    }

    // ── Relative paths: the class of defect, closed ────────────────

    [Theory]
    [InlineData("Agent:Logging:LogFilePath", "logs/agent-.log")]
    [InlineData("Agent:Database:ConnectionString", "Data Source=agent.db")]
    [InlineData("Agent:Database:KeyDirectory", "keys")]
    [InlineData("Agent:Database:KeyDirectory", "%RG_UNDEFINED_VARIABLE%\\keys")]
    public void Relative_or_unresolved_path_is_refused_naming_the_key(string key, string value)
    {
        AgentConfigurationLoadResult result = Load(With(key, value));

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldContain(e => e.StartsWith(key + ":", StringComparison.Ordinal));
    }

    // ── Enum-valued settings (AGT-CFG-001) ─────────────────────────

    [Theory]
    [InlineData("Stirct")]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("Audit,Strict")]
    [InlineData("")]
    public void Unknown_UsbGuard_mode_is_refused_listing_the_allowed_values(string mode)
    {
        AgentConfigurationLoadResult result = Load(With("Agent:UsbGuard:OperatingMode", mode));

        result.Configuration.ShouldBeNull();
        string error = result.Errors.ShouldHaveSingleItem();
        error.ShouldStartWith("Agent:UsbGuard:OperatingMode:");
        error.ShouldContain("Audit, Permissive, Strict");
    }

    [Theory]
    [InlineData("Strict")]
    [InlineData("strict")]
    [InlineData("Audit")]
    public void Defined_UsbGuard_mode_is_accepted(string mode)
    {
        Load(With("Agent:UsbGuard:OperatingMode", mode)).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("5")]
    [InlineData("Bogus")]
    [InlineData("TcpMock,SerialPort")]
    public void Unknown_IronClad_communication_mode_is_refused_listing_the_allowed_values(string mode)
    {
        AgentConfigurationLoadResult result = Load(With("IronClad:CommunicationMode", mode));

        result.Configuration.ShouldBeNull();
        string error = result.Errors.ShouldHaveSingleItem();
        error.ShouldStartWith("IronClad:CommunicationMode:");
        error.ShouldContain("TcpMock, SerialPort");
    }

    [Fact]
    public void Absent_IronClad_section_is_accepted()
    {
        Load(ValidSettings()).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Numeric_log_level_is_refused()
    {
        AgentConfigurationLoadResult result = Load(With("Agent:Logging:MinimumLevel", "5"));

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldContain(e => e.StartsWith("Agent:Logging:MinimumLevel:", StringComparison.Ordinal));
    }

    // ── The TLS rule lives in the loader too (AGT-TLS-001) ─────────

    [Fact]
    public void TrustAnyCertificate_outside_Development_is_refused_by_the_loader()
    {
        AgentConfigurationLoadResult result = Load(With("Agent:Server:TrustAnyCertificate", "true"), "Production");

        result.Configuration.ShouldBeNull();
        result.Errors.ShouldContain(e => e.Contains(ServerTlsPolicy.SettingKey, StringComparison.Ordinal));
    }

    [Fact]
    public void TrustAnyCertificate_in_Development_on_localhost_is_accepted_by_the_loader()
    {
        Dictionary<string, string?> settings = With("Agent:Server:TrustAnyCertificate", "true");
        settings["Agent:Server:BaseUrl"] = "https://localhost";

        Load(settings, "Development").Errors.ShouldBeEmpty();
    }
}
