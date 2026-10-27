#pragma warning disable CS1591 // XML comments not needed for JSON DTO records
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RansomGuard.Agent.Core.Communication.Models;

// ─── Enrollment ────────────────────────────────────────────────────

/// <summary>GRID enrollment request DTO.</summary>
public sealed record EnrollmentRequest
{
    [JsonPropertyName("otp")]
    public required string Otp { get; init; }

    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }

    [JsonPropertyName("fqdn")]
    public required string Fqdn { get; init; }

    [JsonPropertyName("os_version")]
    public required string OsVersion { get; init; }

    [JsonPropertyName("agent_version")]
    public required string AgentVersion { get; init; }

    [JsonPropertyName("hardware_fingerprint")]
    public required string HardwareFingerprint { get; init; }

    /// <summary>
    /// The agent's own audit-signing public key (raw 32-byte Ed25519, base64): the GRID verifies
    /// the audit chain with it. The private key never leaves the endpoint (GRID-SEC-003).
    /// </summary>
    [JsonPropertyName("ed25519_public_key_b64")]
    public required string Ed25519PublicKeyB64 { get; init; }
}

public sealed record EnrollmentResponse
{
    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

// ─── Heartbeat ─────────────────────────────────────────────────────

public sealed record HeartbeatRequest
{
    /// <summary>Payload version. Absent/1 = the legacy 3-field heartbeat; 2 = this one.</summary>
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("agent_uptime_seconds")]
    public required int AgentUptimeSeconds { get; init; }

    [JsonPropertyName("agent_version")]
    public string? AgentVersion { get; init; }

    [JsonPropertyName("os_version")]
    public string? OsVersion { get; init; }

    [JsonPropertyName("threat_intel_version")]
    public string? ThreatIntelVersion { get; init; }

    /// <summary>Last local detection, UTC. Null if none since start.</summary>
    [JsonPropertyName("last_detection_at")]
    public DateTime? LastDetectionAt { get; init; }

    [JsonPropertyName("disk")]
    public DiskInfo? Disk { get; init; }

    [JsonPropertyName("transmission")]
    public TransmissionInfo? Transmission { get; init; }

    /// <summary>Runtime state of every protection module (includes anti-tampering).</summary>
    [JsonPropertyName("modules")]
    public List<ModuleStatusInfo> Modules { get; init; } = [];
}

/// <summary>Free space on the volumes that matter, in bytes. Null = could not be measured, never 0.</summary>
public sealed record DiskInfo
{
    /// <summary>Free bytes on the system volume.</summary>
    [JsonPropertyName("system_free_bytes")]
    public long? SystemFreeBytes { get; init; }

    /// <summary>Free bytes on the volume holding the agent database (matters for Sprint 10 backups).</summary>
    [JsonPropertyName("db_volume_free_bytes")]
    public long? DbVolumeFreeBytes { get; init; }
}

/// <summary>Alert transmission health. Answers "did everything it detected reach the GRID?".</summary>
public sealed record TransmissionInfo
{
    /// <summary>
    /// Alerts waiting in the local upload queue. Null when the local database could not be
    /// read: 0 would tell the GRID "the queue is empty" and could turn the verdict green.
    /// </summary>
    [JsonPropertyName("pending_alerts")]
    public int? PendingAlerts { get; init; }

    /// <summary>Last successful upload, UTC. Null if none yet.</summary>
    [JsonPropertyName("last_upload_ok_at")]
    public DateTime? LastUploadOkAt { get; init; }

    /// <summary>
    /// Age of the last successful upload, in seconds, measured on the agent's monotonic
    /// clock at send time. Immune to clock skew and wall-clock changes; the GRID prefers
    /// it to last_upload_ok_at. Null if no upload has succeeded since startup.
    /// </summary>
    [JsonPropertyName("last_upload_ok_age_seconds")]
    public long? LastUploadOkAgeSeconds { get; init; }

    /// <summary>Stable code of the last upload failure (never an exception message). Null if none.</summary>
    [JsonPropertyName("last_upload_error_code")]
    public string? LastUploadErrorCode { get; init; }

    /// <summary>
    /// Alerts the GRID refused (400/422, not retried) over the last 24 hours. Null when
    /// the local database could not be read — never 0 for "unknown".
    /// </summary>
    [JsonPropertyName("rejected_alerts")]
    public int? RejectedAlerts { get; init; }

    /// <summary>
    /// Audit entries the agent could not record since it started (AGT-AUD-002): the detection
    /// went on, the trace did not. Null when not measured, never 0 for "unknown".
    /// </summary>
    [JsonPropertyName("audit_write_failures")]
    public int? AuditWriteFailures { get; init; }
}

/// <summary>One module's runtime state.</summary>
public sealed record ModuleStatusInfo
{
    /// <summary>Stable module code (SENTINEL, ENTROPY, ...).</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>active | inactive | disabled_by_config | degraded.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>Stable reason code, or null when active.</summary>
    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    /// <summary>When the state last changed, UTC.</summary>
    [JsonPropertyName("changed_at")]
    public DateTime ChangedAt { get; init; }
}

public sealed record HeartbeatResponse
{
    [JsonPropertyName("next_heartbeat_in_seconds")]
    public int NextHeartbeatInSeconds { get; init; }

    [JsonPropertyName("pending_commands")]
    public List<string> PendingCommands { get; init; } = [];

    [JsonPropertyName("server_time")]
    public DateTime ServerTime { get; init; }
}

// ─── Alert Ingestion ───────────────────────────────────────────────

/// <summary>
/// The attribution complement of a SENTINEL alert (ADR-025, section 3): sent after the alert, to
/// POST /api/v1/agents/{id}/alerts/{client_message_id}/attribution. Fields bounded by the GRID.
/// </summary>
public sealed record AttributionComplementRequest
{
    /// <summary>resolved, unavailable or ambiguous.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>The writer's PID; only when resolved.</summary>
    [JsonPropertyName("pid")]
    public int? Pid { get; init; }

    /// <summary>The writer's creation time (UTC); only when resolved.</summary>
    [JsonPropertyName("process_created_at")]
    public DateTime? ProcessCreatedAt { get; init; }

    /// <summary>The writer's process name, at most 260 characters; when known.</summary>
    [JsonPropertyName("process_name")]
    public string? ProcessName { get; init; }

    /// <summary>The writer's image path, at most 1 024 characters; when known.</summary>
    [JsonPropertyName("image_path")]
    public string? ImagePath { get; init; }


/// <summary>Closed reason code when unavailable (no_write_event, system_pid, pid_reused, process_exited, etw_unavailable).</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record AlertDetailInput
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

public sealed record AlertArtifactInput
{
    [JsonPropertyName("artifact_type")]
    public required string ArtifactType { get; init; }

    [JsonPropertyName("artifact_hash_sha256")]
    public required string ArtifactHashSha256 { get; init; }

    [JsonPropertyName("artifact_metadata")]
    public Dictionary<string, string> ArtifactMetadata { get; init; } = new();
}

public sealed record AlertIngestRequest
{
    [JsonPropertyName("client_message_id")]
    public required string ClientMessageId { get; init; }

    [JsonPropertyName("alert_type")]
    public required string AlertType { get; init; }

    [JsonPropertyName("mitre_technique_id")]
    public string? MitreTechniqueId { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("detected_at")]
    public required DateTime DetectedAt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("details")]
    public List<AlertDetailInput> Details { get; init; } = [];

    [JsonPropertyName("artifacts")]
    public List<AlertArtifactInput> Artifacts { get; init; } = [];

    [JsonPropertyName("raw_payload")]
    public Dictionary<string, object> RawPayload { get; init; } = new();
}

public sealed record AlertIngestResponse
{
    [JsonPropertyName("alert_id")]
    public required string AlertId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("ingested_at")]
    public DateTime IngestedAt { get; init; }
}

// ─── Audit Log ─────────────────────────────────────────────────────

public sealed record AuditLogEntryInput
{
    [JsonPropertyName("sequence_number")]
    public required long SequenceNumber { get; init; }

    [JsonPropertyName("payload")]
    public required Dictionary<string, System.Text.Json.JsonElement> Payload { get; init; }

    [JsonPropertyName("ed25519_signature_base64")]
    public required string Ed25519SignatureBase64 { get; init; }

    [JsonPropertyName("signing_key_id")]
    public required string SigningKeyId { get; init; }

    /// <summary>
    /// When the entry was recorded on the workstation. Required by the GRID's schema, which
    /// forbids unknown fields and refuses a missing one -- so an agent that leaves it out is
    /// answered 422 and never gets as far as having its signature checked.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }
}

public sealed record AuditLogBatchRequest
{
    [JsonPropertyName("entries")]
    public required List<AuditLogEntryInput> Entries { get; init; }
}

public sealed record AuditLogBatchResponse
{
    [JsonPropertyName("accepted_count")]
    public int AcceptedCount { get; init; }

    [JsonPropertyName("rejected_count")]
    public int RejectedCount { get; init; }

    [JsonPropertyName("rejected_reasons")]
    public List<string> RejectedReasons { get; init; } = [];
}

// ─── Commands (plan de finition, J1 § 1.3) ───────────────────────────────────

/// <summary>A command the console queued for this agent.</summary>
public sealed record AgentCommand
{
    /// <summary>The command id, named in the result.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>suspend_process, resume_process, isolate, …</summary>
    [JsonPropertyName("command_type")]
    public required string CommandType { get; init; }

    /// <summary>The parameters as the GRID stored them.</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];

    /// <summary>When the console issued it.</summary>
    [JsonPropertyName("issued_at")]
    public DateTime IssuedAt { get; init; }

    /// <summary>A string (or number, as text) parameter; null when absent.</summary>
    public string? Text(string key) =>
        Parameters.TryGetValue(key, out JsonElement element)
            ? element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>An integer parameter; null when absent or not an integer.</summary>
    public int? Int(string key) =>
        Parameters.TryGetValue(key, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)
            ? value
            : null;
}

/// <summary>The open commands of this agent.</summary>
public sealed record AgentCommandsResponse
{
    /// <summary>Oldest first.</summary>
    [JsonPropertyName("commands")]
    public List<AgentCommand> Commands { get; init; } = [];
}

/// <summary>What the agent did with a command: executed, or not, with a closed reason.</summary>
public sealed record CommandResultRequest
{
    /// <summary>True when the command was carried out.</summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>Scalars only: reason, detail, state, suspension_id.</summary>
    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; init; } = [];

    /// <summary>An executed command, with what it produced.</summary>
    public static CommandResultRequest Executed(params (string Key, object? Value)[] payload) =>
        new() { Success = true, Payload = payload.ToDictionary(p => p.Key, p => p.Value) };

    /// <summary>A command not carried out, and why.</summary>
    public static CommandResultRequest NotExecuted(string reason, string? detail = null) =>
        new()
        {
            Success = false,
            Payload = detail is null
                ? new Dictionary<string, object?> { ["reason"] = reason }
                : new Dictionary<string, object?> { ["reason"] = reason, ["detail"] = detail },
        };
}

/// <summary>One process in the chain behind a write.</summary>
public sealed class GenealogyProcessDto
{
    /// <summary>The process id.</summary>
    [JsonPropertyName("pid")]
    public required int Pid { get; init; }

    /// <summary>Its image name, without the path.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Its full image path, when it could be read.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>When it started, which is what distinguishes it from a reused PID.</summary>
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; init; }

    /// <summary>Its command line, when it could be read.</summary>
    [JsonPropertyName("command_line")]
    public string? CommandLine { get; init; }
}

/// <summary>A MITRE ATT&amp;CK technique the chain matched.</summary>
public sealed class GenealogyPatternDto
{
    /// <summary>The technique id, for example T1486.</summary>
    [JsonPropertyName("technique_id")]
    public required string TechniqueId { get; init; }

    /// <summary>Its name.</summary>
    [JsonPropertyName("technique_name")]
    public required string TechniqueName { get; init; }

    /// <summary>Why this chain matched it.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// The chain of processes behind an alert, sent after the alert itself: the tree is built while
/// the writer is still running, which is necessarily later.
/// </summary>
public sealed class GenealogyComplementRequest
{
    /// <summary>The process that wrote the file.</summary>
    [JsonPropertyName("writer")]
    public required GenealogyProcessDto Writer { get; init; }

    /// <summary>Its parent, grandparent and so on, nearest first.</summary>
    [JsonPropertyName("ancestors")]
    public List<GenealogyProcessDto> Ancestors { get; init; } = [];

    /// <summary>The techniques this chain matched.</summary>
    [JsonPropertyName("patterns")]
    public List<GenealogyPatternDto> Patterns { get; init; } = [];
}
