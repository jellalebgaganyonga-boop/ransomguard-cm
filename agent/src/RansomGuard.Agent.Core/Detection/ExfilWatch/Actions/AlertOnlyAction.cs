using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Logs an exfiltration alert to the audit log with Ed25519 signature.
/// Always available — the baseline response for all severity levels.
/// </summary>
public sealed class AlertOnlyAction : IExfilAction
{
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<AlertOnlyAction> _logger;

    /// <inheritdoc />
    public string ActionName => "AlertOnly";

    /// <summary>Initializes the alert action with audit log dependency.</summary>
    public AlertOnlyAction(IAuditLogRepository auditLog, ILogger<AlertOnlyAction> logger)
    {
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExfilActionResult> ExecuteAsync(ExfilFinding finding, CancellationToken ct)
    {
        string details = $"[{finding.Severity}] {finding.RuleName}: " +
                         $"Process={finding.ProcessName} (PID {finding.ProcessId}), " +
                         $"Dest={finding.Destination}, Bytes={finding.BytesTransferred}, " +
                         $"MITRE={finding.MitreId}";

        await _auditLog.AppendAsync(
            "ExfilAlert",
            details,
            entityType: "ExfilFinding",
            cancellationToken: ct);

        _logger.LogWarning("EXFIL ACTION [AlertOnly]: {Details}", details);

        return new ExfilActionResult
        {
            Success = true,
            ActionName = ActionName,
            Details = details
        };
    }
}
