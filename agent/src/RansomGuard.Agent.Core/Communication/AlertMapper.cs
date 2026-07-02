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
    public static AlertIngestRequest FromCanaryAlert(CanaryAlert alert)
    {
        var details = new List<AlertDetailInput>();
        if (alert.OffendingProcessId is not null)
            details.Add(new AlertDetailInput { Key = "process_id", Value = alert.OffendingProcessId.Value.ToString() });
        if (alert.OffendingProcessName is not null)
            details.Add(new AlertDetailInput { Key = "process_name", Value = alert.OffendingProcessName });
        if (alert.OffendingProcessPath is not null)
            details.Add(new AlertDetailInput { Key = "process_path", Value = Truncate(alert.OffendingProcessPath, 2000) });

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
