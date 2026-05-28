namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Abstraction for Windows Firewall operations.
/// Production implementation uses INetFwPolicy2 COM interop.
/// </summary>
public interface IFirewallManager
{
    /// <summary>
    /// Creates an outbound block rule for the specified IP address.
    /// Rule name encodes creation time for expiry tracking.
    /// </summary>
    /// <returns>True if the rule was created successfully.</returns>
    bool AddBlockRule(string ruleName, string ipAddress);

    /// <summary>
    /// Removes a firewall rule by name.
    /// </summary>
    /// <returns>True if the rule was removed (or did not exist).</returns>
    bool RemoveRule(string ruleName);

    /// <summary>
    /// Gets all RansomGuard-managed firewall rule names.
    /// </summary>
    IReadOnlyList<string> GetManagedRuleNames();
}
