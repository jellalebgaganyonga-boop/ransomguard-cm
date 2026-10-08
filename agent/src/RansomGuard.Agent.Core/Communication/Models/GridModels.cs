#pragma warning disable CS1591 // XML comments not needed for JSON DTO records
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
}

public sealed record EnrollmentResponse
{
    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("ed25519_private_key_pem")]
    public string? Ed25519PrivateKeyPem { get; init; }
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

/// <summary>Free space on the volumes that matter, in bytes.</summary>
public sealed record DiskInfo
{
    /// <summary>Free bytes on the system volume.</summary>
    [JsonPropertyName("system_free_bytes")]
    public long SystemFreeBytes { get; init; }

    /// <summary>Free bytes on the volume holding the agent database (matters for Sprint 10 backups).</summary>
    [JsonPropertyName("db_volume_free_bytes")]
    public long DbVolumeFreeBytes { get; init; }
}

/// <summary>Alert transmission health. Answers "did everything it detected reach the GRID?".</summary>
public sealed record TransmissionInfo
{
    /// <summary>Alerts waiting in the local upload queue.</summary>
    [JsonPropertyName("pending_alerts")]
    public int PendingAlerts { get; init; }

    /// <summary>Last successful upload, UTC. Null if none yet.</summary>
    [JsonPropertyName("last_upload_ok_at")]
    public DateTime? LastUploadOkAt { get; init; }

    /// <summary>Stable code of the last upload failure (never an exception message). Null if none.</summary>
    [JsonPropertyName("last_upload_error_code")]
    public string? LastUploadErrorCode { get; init; }
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
    public required Dictionary<string, object> Payload { get; init; }

    [JsonPropertyName("ed25519_signature_base64")]
    public required string Ed25519SignatureBase64 { get; init; }

    [JsonPropertyName("signing_key_id")]
    public required string SigningKeyId { get; init; }
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
