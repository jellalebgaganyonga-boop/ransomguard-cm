namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Provides threat intelligence for exfiltration detection rules.
/// Loads from static JSON data files shipped with the agent.
/// Updated via signed threat intel packages (Sprint 4 B.5).
/// </summary>
public interface IThreatIntelProvider
{
    /// <summary>Returns true if the destination is on the organization whitelist.</summary>
    bool IsWhitelistedDestination(string remoteAddress);

    /// <summary>Returns true if the destination is a known cloud provider IP.</summary>
    bool IsKnownCloudProvider(string remoteAddress);

    /// <summary>Returns true if the IP is a known Tor exit node.</summary>
    bool IsTorExitNode(string remoteAddress);

    /// <summary>Returns true if the IP is a known C2 server.</summary>
    bool IsKnownC2Server(string remoteAddress);

    /// <summary>Returns true if the process name is a known LOLBAS binary.</summary>
    bool IsLolbasBinary(string processName);

    /// <summary>Current version of threat intel data.</summary>
    string Version { get; }

    /// <summary>Reloads threat intel data from the specified directory (for updates).</summary>
    void ReloadFromDirectory(string dataDirectory);
}
