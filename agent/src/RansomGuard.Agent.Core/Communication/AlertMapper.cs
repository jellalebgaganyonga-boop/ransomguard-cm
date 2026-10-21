using System.Security.Cryptography;
using System.Text;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// Maps module-specific alert entities to the unified AlertIngestRequest for GRID forwarding.
/// Each method produces a deterministic client_message_id for idempotent retry.
/// </summary>
public static class AlertMapper
{
    /// <summary>Map a SENTINEL canary alert to an AlertIngestRequest.</summary>
    public static AlertIngestRequest FromCanaryAlert(CanaryAlert alert, string? attributionState = null, string? attributionReason = null)
    {
        var details = new List<AlertDetailInput>();
        if (alert.OffendingProcessId is not null)
            details.Add(new AlertDetailInput { Key = "process_id", Value = alert.OffendingProcessId.Value.ToString() });
        if (alert.OffendingProcessName is not null)
            details.Add(new AlertDetailInput { Key = "process_name", Value = alert.OffendingProcessName });
        if (alert.OffendingProcessPath is not null)
            details.Add(new AlertDetailInput { Key = "process_path", Value = Truncate(alert.OffendingProcessPath, 2000) });

        // SENTINEL's attribution (ADR-025): "pending" while the complement is on its way,
        // "unavailable" with its closed reason when nobody will be named.
        if (attributionState is not null)
            details.Add(new AlertDetailInput { Key = "attribution_state", Value = attributionState });
        if (attributionReason is not null)
            details.Add(new AlertDetailInput { Key = "attribution_reason", Value = attributionReason });

        return new AlertIngestRequest
        {
            ClientMessageId = DeterministicId(alert.Id),
            AlertType = $"Sentinel{alert.AlertType}",
            MitreTechniqueId = "T1486",
            Severity = alert.Severity.ToString(),
            DetectedAt = alert.DetectedAt,
            Summary = Truncate($"Canary file {alert.AlertType.ToString().ToLowerInvariant()}: {alert.CanaryPath}", 500),
            Details = details,
            RawPayload = new Dictionary<string, object>
            {
                ["canary_id"] = alert.CanaryId.ToString(),
                ["canary_path"] = alert.CanaryPath,
            },
        };
    }

    /// <summary>Map an ENTROPY alert to an AlertIngestRequest.</summary>
    public static AlertIngestRequest? FromEntropyAlert(EntropyAlert alert)
    {
        // Suppressed alerts are not forwarded to GRID
        if (string.Equals(alert.Severity, "Suppressed", StringComparison.OrdinalIgnoreCase))
            return null;

        return new AlertIngestRequest
        {
            ClientMessageId = DeterministicId(alert.Id),
            AlertType = $"Entropy{alert.RuleName}",
            MitreTechniqueId = "T1486",
            Severity = alert.Severity,
            DetectedAt = alert.DetectedAt,
            Summary = Truncate($"Entropy spike on {alert.FilePath}: {alert.BaselineEntropy:F2} -> {alert.CurrentEntropy:F2} ({alert.RuleName})", 500),
            Details =
            [
                new AlertDetailInput { Key = "baseline_entropy", Value = alert.BaselineEntropy.ToString("F4") },
                new AlertDetailInput { Key = "current_entropy", Value = alert.CurrentEntropy.ToString("F4") },
                new AlertDetailInput { Key = "delta", Value = alert.Delta.ToString("F4") },
                new AlertDetailInput { Key = "rule_id", Value = alert.RuleId.ToString() },
            ],
            RawPayload = new Dictionary<string, object>
            {
                ["file_path"] = alert.FilePath,
                ["rule_id"] = alert.RuleId,
            },
        };
    }

    /// <summary>Map a USB GUARD alert to an AlertIngestRequest.</summary>
    public static AlertIngestRequest FromUsbAlert(UsbAlert alert)
    {
        return new AlertIngestRequest
        {
            ClientMessageId = DeterministicId(alert.Id),
            AlertType = "UsbGuard",
            MitreTechniqueId = "T1091",
            Severity = alert.Severity,
            DetectedAt = alert.GeneratedAt,
            Summary = Truncate(alert.Title, 500),
            Details =
            [
                new AlertDetailInput { Key = "action_taken", Value = alert.ActionTaken },
                new AlertDetailInput { Key = "description", Value = Truncate(alert.Description, 2000) },
            ],
            RawPayload = new Dictionary<string, object>
            {
                ["scan_result_id"] = alert.UsbScanResultId.ToString(),
            },
        };
    }

    /// <summary>Map an EXFIL WATCH finding to an AlertIngestRequest.</summary>
    public static AlertIngestRequest FromExfilFinding(ExfilFinding finding)
    {
        return new AlertIngestRequest
        {
            ClientMessageId = DeterministicExfilId(finding),
            AlertType = $"ExfilWatch{finding.RuleName}",
            MitreTechniqueId = Truncate(finding.MitreId, 20),
            Severity = finding.Severity.ToString(),
            DetectedAt = finding.DetectedAt,
            Summary = Truncate(finding.Description, 500),
            Details =
            [
                new AlertDetailInput { Key = "process_id", Value = finding.ProcessId.ToString() },
                new AlertDetailInput { Key = "process_name", Value = finding.ProcessName },
                new AlertDetailInput { Key = "destination", Value = Truncate(finding.Destination, 2000) },
                new AlertDetailInput { Key = "bytes_transferred", Value = finding.BytesTransferred.ToString() },
            ],
            RawPayload = new Dictionary<string, object>
            {
                ["destination_port"] = finding.DestinationPort ?? 0,
                ["bytes_transferred"] = finding.BytesTransferred,
            },
        };
    }

    /// <summary>Map an INDICATOR REMOVAL event to an AlertIngestRequest.</summary>
    public static AlertIngestRequest FromIndicatorRemovalEvent(IndicatorRemovalEvent evt)
    {
        return new AlertIngestRequest
        {
            ClientMessageId = DeterministicId(evt.Id),
            AlertType = $"IndicatorRemoval{evt.EventType}",
            MitreTechniqueId = Truncate(evt.MitreTechniqueId, 20),
            Severity = evt.Severity,
            DetectedAt = evt.DetectedAt,
            Summary = Truncate(evt.Description, 500),
            Details =
            [
                new AlertDetailInput { Key = "process_id", Value = evt.ProcessId.ToString() },
                new AlertDetailInput { Key = "process_name", Value = evt.ProcessName },
                new AlertDetailInput { Key = "command_line", Value = Truncate(evt.CommandLine, 2000) },
                new AlertDetailInput { Key = "target_resource", Value = Truncate(evt.TargetResource, 2000) },
                new AlertDetailInput { Key = "action_taken", Value = evt.ActionTaken },
            ],
            RawPayload = new Dictionary<string, object>
            {
                ["kill_chain_correlation_id"] = evt.KillChainCorrelationId?.ToString() ?? "",
            },
        };
    }

    /// <summary>The response to a canary write (ADR-026): the writer is suspended. Linked to its alert by response_to.</summary>
    public static AlertIngestRequest FromProcessSuspension(SuspendedProcess suspension) => new()
    {
        ClientMessageId = DeterministicId(suspension.Id),
        AlertType = "SentinelProcessSuspended",
        MitreTechniqueId = "T1486",
        Severity = "Critical",
        DetectedAt = suspension.SuspendedAt,
        Summary = Truncate($"Process suspended after writing a canary: {suspension.ProcessName ?? "(unnamed)"} (PID {suspension.ProcessId}) on {suspension.CanaryPath}", 500),
        Details =
        [
            new AlertDetailInput { Key = "response", Value = "process_suspended" },
            new AlertDetailInput { Key = "suspension_id", Value = suspension.Id.ToString() },
            new AlertDetailInput { Key = "response_to", Value = suspension.ClientMessageId },
            new AlertDetailInput { Key = "process_id", Value = suspension.ProcessId.ToString() },
            new AlertDetailInput { Key = "process_created_at", Value = suspension.ProcessCreatedAt.ToString("o") },
            new AlertDetailInput { Key = "process_name", Value = Truncate(suspension.ProcessName ?? "", 260) },
            new AlertDetailInput { Key = "process_path", Value = Truncate(suspension.ImagePath ?? "", 1024) },
        ],
        RawPayload = new Dictionary<string, object>
        {
            ["suspension_id"] = suspension.Id.ToString(),
            ["canary_path"] = suspension.CanaryPath,
        },
    };

    /// <summary>A suspension that was decided and could not be done; the console must know the writer runs on.</summary>
    public static AlertIngestRequest FromSuspensionFailed(
        Guid id, int pid, DateTime createdAt, string? processName, string? imagePath, string canaryPath,
        string reason, string? error, string responseTo, DateTime at) => new()
    {
        ClientMessageId = DeterministicId(id),
        AlertType = "SentinelProcessSuspendFailed",
        MitreTechniqueId = "T1486",
        Severity = "Critical",
        DetectedAt = at,
        Summary = Truncate($"Suspension failed ({reason}): {processName ?? "(unnamed)"} (PID {pid}) still runs after writing {canaryPath}", 500),
        Details =
        [
            new AlertDetailInput { Key = "response", Value = "suspend_failed" },
            new AlertDetailInput { Key = "reason", Value = reason },
            new AlertDetailInput { Key = "error", Value = Truncate(error ?? "", 2000) },
            new AlertDetailInput { Key = "response_to", Value = responseTo },
            new AlertDetailInput { Key = "process_id", Value = pid.ToString() },
            new AlertDetailInput { Key = "process_created_at", Value = createdAt.ToString("o") },
            new AlertDetailInput { Key = "process_name", Value = Truncate(processName ?? "", 260) },
            new AlertDetailInput { Key = "process_path", Value = Truncate(imagePath ?? "", 1024) },
        ],
        RawPayload = new Dictionary<string, object> { ["canary_path"] = canaryPath },
    };

    /// <summary>A suspended process waiting for a decision past the escalation delay (ADR-026, 2c). One per escalation.</summary>
    public static AlertIngestRequest FromSuspensionEscalation(SuspendedProcess suspension, TimeSpan age, int escalation, DateTime at) => new()
    {
        ClientMessageId = $"{DeterministicId(suspension.Id)}-e{escalation}",
        AlertType = "SentinelProcessSuspendedNoDecision",
        MitreTechniqueId = "T1486",
        Severity = "Critical",
        DetectedAt = at,
        Summary = Truncate($"Suspended for {(int)age.TotalMinutes} min with no decision: {suspension.ProcessName ?? "(unnamed)"} (PID {suspension.ProcessId}); resume or keep it, nothing resumes by itself", 500),
        Details =
        [
            new AlertDetailInput { Key = "response", Value = "suspended_no_decision" },
            new AlertDetailInput { Key = "suspension_id", Value = suspension.Id.ToString() },
            new AlertDetailInput { Key = "response_to", Value = suspension.ClientMessageId },
            new AlertDetailInput { Key = "suspended_for_minutes", Value = ((int)age.TotalMinutes).ToString() },
            new AlertDetailInput { Key = "escalation", Value = escalation.ToString() },
            new AlertDetailInput { Key = "process_id", Value = suspension.ProcessId.ToString() },
            new AlertDetailInput { Key = "process_name", Value = Truncate(suspension.ProcessName ?? "", 260) },
        ],
        RawPayload = new Dictionary<string, object> { ["suspension_id"] = suspension.Id.ToString() },
    };

    /// <summary>A burst of unattributed canaries: nobody to suspend, the operator is told to isolate (ADR-026, 2d).</summary>
    public static AlertIngestRequest FromIsolationRecommended(Guid id, IReadOnlyList<string> canaryPaths, DateTime at) => new()
    {
        ClientMessageId = DeterministicId(id),
        AlertType = "SentinelIsolationRecommended",
        MitreTechniqueId = "T1486",
        Severity = "Critical",
        DetectedAt = at,
        Summary = Truncate($"{canaryPaths.Count} canaries written in 60 s with no process named: isolate this endpoint (manual, the agent cannot)", 500),
        Details =
        [
            new AlertDetailInput { Key = "response", Value = "isolation_recommended" },
            new AlertDetailInput { Key = "canaries", Value = canaryPaths.Count.ToString() },
            new AlertDetailInput { Key = "canary_paths", Value = Truncate(string.Join("; ", canaryPaths), 2000) },
        ],
        RawPayload = new Dictionary<string, object> { ["canary_paths"] = canaryPaths.ToList() },
    };

    /// <summary>
    /// Deterministic client_message_id from a Guid.
    /// SHA256 hex of the Guid string = 64 chars (within GRID constraint 10-64).
    /// </summary>
    public static string DeterministicId(Guid id)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Deterministic client_message_id for ExfilFinding (no Guid).
    /// Deduplicates same rule+process+destination within the same minute.
    /// </summary>
    public static string DeterministicExfilId(ExfilFinding finding)
    {
        // Round to minute for intentional deduplication of repeated triggers
        string minuteKey = finding.DetectedAt.ToString("yyyyMMddHHmm");
        string composite = $"{finding.RuleName}|{finding.ProcessId}|{finding.Destination}|{minuteKey}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
