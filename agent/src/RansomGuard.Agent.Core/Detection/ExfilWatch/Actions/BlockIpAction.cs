using System.Globalization;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Blocks a suspect destination IP using Windows Firewall via INetFwPolicy2 COM interop.
/// Rule auto-expires after 24 hours (cleaned by ExfilFirewallRuleCleanupService).
/// Enabled for severity Critical only.
/// </summary>
public sealed class BlockIpAction : IExfilAction
{
    /// <summary>Prefix for all RansomGuard-managed firewall rules.</summary>
    public const string RuleNamePrefix = "RansomGuard-Block-";

    /// <summary>Timestamp format encoded in rule names for expiry tracking.</summary>
    public const string TimestampFormat = "yyyyMMddHHmmss";

    private readonly IFirewallManager _firewall;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<BlockIpAction> _logger;

    /// <inheritdoc />
    public string ActionName => "BlockIp";

    /// <summary>Initializes the block action.</summary>
    public BlockIpAction(
        IFirewallManager firewall,
        IAuditLogRepository auditLog,
        ILogger<BlockIpAction> logger)
    {
        _firewall = firewall;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExfilActionResult> ExecuteAsync(ExfilFinding finding, CancellationToken ct)
    {
        string timestamp = DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string ruleName = $"{RuleNamePrefix}{finding.Destination}-{timestamp}";

        bool blocked = _firewall.AddBlockRule(ruleName, finding.Destination);

        string details = $"Dest={finding.Destination}, Rule={ruleName}, Blocked={blocked}";

        await _auditLog.AppendAsync(
            blocked ? "ExfilBlock" : "ExfilBlockFailed",
            details,
            entityType: "ExfilFinding",
            cancellationToken: ct);

        if (blocked)
            _logger.LogWarning("EXFIL ACTION [BlockIp]: {Details}", details);
        else
            _logger.LogError("EXFIL ACTION [BlockIp] FAILED: {Details}", details);

        return new ExfilActionResult
        {
            Success = blocked,
            ActionName = ActionName,
            Details = details
        };
    }

    /// <summary>
    /// Parses the creation timestamp from a managed rule name.
    /// </summary>
    public static DateTime? ParseRuleTimestamp(string ruleName)
    {
        if (!ruleName.StartsWith(RuleNamePrefix, StringComparison.Ordinal))
            return null;

        // Format: RansomGuard-Block-{IP}-{yyyyMMddHHmmss}
        int lastDash = ruleName.LastIndexOf('-');
        if (lastDash < 0 || lastDash >= ruleName.Length - 1)
            return null;

        string timestampStr = ruleName[(lastDash + 1)..];
        if (DateTime.TryParseExact(timestampStr, TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result))
        {
            return result;
        }

        return null;
    }
}
