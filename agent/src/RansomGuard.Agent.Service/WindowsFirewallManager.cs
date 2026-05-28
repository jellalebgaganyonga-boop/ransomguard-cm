using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Manages Windows Firewall rules via INetFwPolicy2 COM interop (HNetCfg.FwPolicy2).
/// Creates outbound block rules for suspect IP addresses with RansomGuard naming convention.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFirewallManager : IFirewallManager
{
    private const int NetFwActionBlock = 0; // NET_FW_ACTION_BLOCK
    private const int NetFwRuleDirectionOut = 2; // NET_FW_RULE_DIR_OUT
    private const int NetFwIpProtocolAny = 256; // NET_FW_IP_PROTOCOL_ANY

    private readonly ILogger<WindowsFirewallManager> _logger;

    /// <summary>Initializes the firewall manager.</summary>
    public WindowsFirewallManager(ILogger<WindowsFirewallManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool AddBlockRule(string ruleName, string ipAddress)
    {
        try
        {
            dynamic fwPolicy = CreateFirewallPolicy();
            dynamic rule = CreateFirewallRule();

            rule.Name = ruleName;
            rule.Description = $"RansomGuard exfil block — auto-expires after 24h";
            rule.Action = NetFwActionBlock;
            rule.Direction = NetFwRuleDirectionOut;
            rule.Protocol = NetFwIpProtocolAny;
            rule.RemoteAddresses = ipAddress;
            rule.Enabled = true;

            fwPolicy.Rules.Add(rule);

            _logger.LogInformation("Firewall block rule created: {Rule} → {IP}", ruleName, ipAddress);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to create firewall rule (requires admin)");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create firewall rule {Rule}", ruleName);
            return false;
        }
    }

    /// <inheritdoc />
    public bool RemoveRule(string ruleName)
    {
        try
        {
            dynamic fwPolicy = CreateFirewallPolicy();
            fwPolicy.Rules.Remove(ruleName);

            _logger.LogInformation("Firewall rule removed: {Rule}", ruleName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove firewall rule {Rule}", ruleName);
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetManagedRuleNames()
    {
        var names = new List<string>();
        try
        {
            dynamic fwPolicy = CreateFirewallPolicy();
            foreach (dynamic rule in fwPolicy.Rules)
            {
                string name = rule.Name;
                if (name.StartsWith(BlockIpAction.RuleNamePrefix, StringComparison.Ordinal))
                    names.Add(name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate firewall rules");
        }
        return names;
    }

    private static dynamic CreateFirewallPolicy()
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
            ?? throw new InvalidOperationException("Windows Firewall COM component not available");
        return Activator.CreateInstance(policyType)!;
    }

    private static dynamic CreateFirewallRule()
    {
        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
            ?? throw new InvalidOperationException("Windows Firewall COM component not available");
        return Activator.CreateInstance(ruleType)!;
    }
}
