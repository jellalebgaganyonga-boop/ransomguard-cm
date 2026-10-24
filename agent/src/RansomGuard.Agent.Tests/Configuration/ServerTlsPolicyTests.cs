using Microsoft.Extensions.Configuration;
using RansomGuard.Agent.Core.Configuration;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// Agent:Server:TrustAnyCertificate disables TLS validation of the GRID certificate. It may be
/// true only in Development, against a server on this machine (defect AGT-TLS-001).
/// </summary>
public sealed class ServerTlsPolicyTests
{
    private static string ServiceDir => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "RansomGuard.Agent.Service");

    [Fact]
    public void Shipped_appsettings_does_not_trust_any_certificate()
    {
        IConfiguration shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.json"), optional: false)
            .Build();

        shipped.GetValue<bool>(ServerTlsPolicy.SettingKey).ShouldBeFalse();
    }

    [Fact]
    public void Development_appsettings_trusts_any_certificate_only_on_localhost()
    {
        IConfiguration development = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(ServiceDir, "appsettings.Development.json"), optional: false)
            .Build();

        ServerTlsPolicy.Validate(
                development.GetValue<bool>(ServerTlsPolicy.SettingKey),
                development["Agent:Server:BaseUrl"],
                "Development")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void True_outside_Development_is_refused_naming_key_and_environment(string environment)
    {
        string? refusal = ServerTlsPolicy.Validate(true, "https://localhost", environment);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain(ServerTlsPolicy.SettingKey);
        refusal.ShouldContain($"'{environment}'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void True_without_environment_is_refused_as_Production(string? environment)
    {
        string? refusal = ServerTlsPolicy.Validate(true, "https://localhost", environment);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain("'Production'");
    }

    [Theory]
    [InlineData("https://100.117.242.84")]
    [InlineData("https://grid.hopital.cm")]
    [InlineData("https://localhost.evil.example")]
    [InlineData("not a url")]
    public void True_in_Development_with_a_remote_host_is_refused(string baseUrl)
    {
        string? refusal = ServerTlsPolicy.Validate(true, baseUrl, "Development");

        refusal.ShouldNotBeNull();
        refusal.ShouldContain("Agent:Server:BaseUrl");
    }

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://localhost:9443")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://[::1]:8443")]
    public void True_in_Development_on_localhost_is_accepted(string baseUrl)
    {
        ServerTlsPolicy.Validate(true, baseUrl, "Development").ShouldBeNull();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData(null)]
    public void False_is_always_accepted(string? environment)
    {
        ServerTlsPolicy.Validate(false, "https://grid.hopital.cm", environment).ShouldBeNull();
    }
}
