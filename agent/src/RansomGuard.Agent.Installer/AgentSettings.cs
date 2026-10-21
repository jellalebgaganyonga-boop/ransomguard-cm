using System.Text.Json;
using System.Text.Json.Nodes;

namespace RansomGuard.Agent.Installer;

/// <summary>
/// What the installer writes into the shipped <c>appsettings.json</c> (ADR-027, 2c, point 3): the
/// same five values as <c>install.ps1</c>, nothing else. Every other key -- the watched folders
/// (<c>%EACHUSERPROFILE%</c>, AGT-PATH-001), the modules, the response -- stays as shipped.
/// </summary>
public static class AgentSettings
{
    /// <summary>Applies the installer's values to the shipped settings and returns the JSON to write.</summary>
    /// <param name="shippedJson">The appsettings.json of the package.</param>
    /// <param name="gridBaseUrl">The GRID address, from the payload.</param>
    /// <param name="otp">The enrollment code, from the payload.</param>
    /// <param name="hostname">This machine's name.</param>
    /// <param name="dataDir">The agent's data root (database, logs, keys).</param>
    public static string Apply(string shippedJson, string gridBaseUrl, string otp, string hostname, string dataDir)
    {
        JsonNode root = JsonNode.Parse(shippedJson) ?? throw new InvalidDataException("appsettings.json: empty");
        JsonObject agent = root["Agent"] as JsonObject ?? throw new InvalidDataException("appsettings.json: no Agent section");

        JsonObject server = Section(agent, "Server");
        server["BaseUrl"] = gridBaseUrl;          // the shipped file carries no address: added, not merely overwritten
        server["EnrollmentOtp"] = otp;

        Section(agent, "Identity")["Hostname"] = hostname;
        Section(agent, "Database")["ConnectionString"] = $"Data Source={Path.Combine(dataDir, "data", "agent.db")}";
        Section(agent, "Logging")["LogFilePath"] = Path.Combine(dataDir, "logs", "agent-.log");

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Section(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }
}
