using System.Reflection;
using System.Text.Json;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The version the agent reports comes from the build (AssemblyInformationalVersion), never from
/// a setting: in the LOT 2 demo, today's build showed "0.3.0", the July number left in
/// appsettings.json.
/// </summary>
public sealed class AgentVersionTests
{
    private static string ServiceDir => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "RansomGuard.Agent.Service");

    [Fact]
    public void The_version_is_the_informational_version_of_the_running_build()
    {
        string expected = typeof(AgentVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        AgentVersion.Current.ShouldBe(expected);
        AgentVersion.Current.ShouldNotBe("0.3.0");
    }

    [Fact]
    public void The_shipped_settings_carry_no_version()
    {
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(ServiceDir, "appsettings.json")));
        JsonElement identity = settings.RootElement.GetProperty("Agent").GetProperty("Identity");

        identity.TryGetProperty("Version", out _).ShouldBeFalse();
    }
}
