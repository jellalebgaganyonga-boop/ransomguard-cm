using System.Reflection;

namespace RansomGuard.Agent.Service;

/// <summary>
/// The agent's version: the AssemblyInformationalVersion of the build that runs (set at build
/// time: Version, plus the commit, e.g. "1.0.0+240a191e…"). Sent at enrollment and in every
/// heartbeat. It used to come from Agent:Identity:Version in appsettings.json, which nobody
/// updated: a July build and today's both reported 0.3.0.
/// </summary>
public static class AgentVersion
{
    /// <summary>The version of the running agent.</summary>
    public static string Current { get; } =
        typeof(AgentVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AgentVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
