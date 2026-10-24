using System.Text.Json;
using RansomGuard.Agent.Installer;
using Shouldly;

namespace RansomGuard.Agent.Tests.Installer;

/// <summary>ADR-027, step 3: the installer writes five values into the shipped settings and touches nothing else.</summary>
public sealed class AgentSettingsTests
{
    private static readonly string Shipped = File.ReadAllText(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "RansomGuard.Agent.Service", "appsettings.json"));

    [Fact]
    public void The_five_values_of_install_ps1_are_written_and_the_rest_stays_as_shipped()
    {
        string json = AgentSettings.Apply(Shipped, "https://grid.hopital.local:8443", "RG-CAM-2026-ABCDEF", "POSTE-RADIO-01", @"C:\ProgramData\RansomGuard-CM");

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement agent = doc.RootElement.GetProperty("Agent");
        agent.GetProperty("Server").GetProperty("BaseUrl").GetString().ShouldBe("https://grid.hopital.local:8443");
        agent.GetProperty("Server").GetProperty("EnrollmentOtp").GetString().ShouldBe("RG-CAM-2026-ABCDEF");
        agent.GetProperty("Identity").GetProperty("Hostname").GetString().ShouldBe("POSTE-RADIO-01");
        agent.GetProperty("Database").GetProperty("ConnectionString").GetString().ShouldBe(@"Data Source=C:\ProgramData\RansomGuard-CM\data\agent.db");
        agent.GetProperty("Logging").GetProperty("LogFilePath").GetString().ShouldBe(@"C:\ProgramData\RansomGuard-CM\logs\agent-.log");

        // The watched folders are the real users' (AGT-PATH-001), untouched.
        agent.GetProperty("Sentinel").GetProperty("WatchDirectories")[0].GetString().ShouldStartWith("%EACHUSERPROFILE%");
        agent.GetProperty("Detection").GetProperty("WatchPaths")[0].GetString().ShouldStartWith("%EACHUSERPROFILE%");
        // The other server settings of the shipped file stay.
        using JsonDocument shipped = JsonDocument.Parse(Shipped);
        foreach (JsonProperty property in shipped.RootElement.GetProperty("Agent").GetProperty("Server").EnumerateObject())
        {
            if (property.Name is "BaseUrl" or "EnrollmentOtp") continue;
            agent.GetProperty("Server").GetProperty(property.Name).GetRawText().ShouldBe(property.Value.GetRawText(), property.Name);
        }
    }

    [Fact]
    public void A_file_without_the_Agent_section_is_refused_not_silently_completed()
    {
        Should.Throw<InvalidDataException>(() => AgentSettings.Apply("{\"Other\":{}}", "https://g", "RG-CAM-2026-ABCDEF", "H", @"C:\x"));
    }
}
