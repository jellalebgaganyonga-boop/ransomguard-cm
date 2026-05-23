namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Provides threat intelligence for exfiltration detection rules.
/// Current implementation uses static whitelists; future versions
/// will integrate live threat feeds (Sprint 5 B.5 spec).
/// </summary>
public interface IThreatIntelProvider
{
    /// <summary>Returns true if the destination is on the organization whitelist.</summary>
    bool IsWhitelistedDestination(string remoteAddress);

    /// <summary>Returns true if the destination is a known cloud provider IP.</summary>
    bool IsKnownCloudProvider(string remoteAddress);

    /// <summary>Returns true if the IP is a known Tor exit node.</summary>
    bool IsTorExitNode(string remoteAddress);
}

/// <summary>
/// Static whitelist-based implementation of <see cref="IThreatIntelProvider"/>.
/// </summary>
public sealed class StaticThreatIntelProvider : IThreatIntelProvider
{
    private static readonly HashSet<string> WhitelistedDestinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsupdate.microsoft.com", "update.microsoft.com",
        "ctldl.windowsupdate.com", "download.windowsupdate.com",
        "login.microsoftonline.com", "graph.microsoft.com",
        "www.google.com", "googleapis.com",
        "ocsp.digicert.com", "crl.microsoft.com"
    };

    private static readonly string[] CloudPrefixes =
    {
        "3.", "13.", "15.", "18.", "20.", "34.", "35.", "40.", "51.", "52.", "54.", "104."
    };

    private static readonly HashSet<int> TorPorts = new() { 9001, 9030, 9050, 9051, 9150 };

    /// <inheritdoc />
    public bool IsWhitelistedDestination(string remoteAddress)
    {
        return WhitelistedDestinations.Contains(remoteAddress);
    }

    /// <inheritdoc />
    public bool IsKnownCloudProvider(string remoteAddress)
    {
        foreach (var prefix in CloudPrefixes)
        {
            if (remoteAddress.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool IsTorExitNode(string remoteAddress)
    {
        // Static list — in production this would query a Tor exit node database
        return false;
    }
}
