namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Persists alert payloads that failed to send to GRID for resilient retry.
/// Survives agent restarts. Retried with exponential backoff by AlertForwardingService.
/// </summary>
public sealed class PendingAlertUpload
{
    /// <summary>Unique record identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Deterministic alert ID for idempotent GRID deduplication.</summary>
    public required string ClientMessageId { get; init; }

    /// <summary>
    /// An alert, or the attribution complement of an alert (ADR-025). The complement carries its
    /// alert's ClientMessageId: the pair (ClientMessageId, Kind) is the unique key.
    /// </summary>
    public PendingUploadKind Kind { get; init; } = PendingUploadKind.Alert;

    /// <summary>JSON-serialized AlertIngestRequest payload.</summary>
    public required string SerializedPayload { get; init; }

    /// <summary>UTC timestamp of the first send attempt.</summary>
    public required DateTime FirstAttemptAt { get; init; }

    /// <summary>UTC timestamp when the next retry should be attempted.</summary>
    public DateTime NextRetryAt { get; set; }

    /// <summary>Number of send attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Current upload status.</summary>
    public PendingUploadStatus Status { get; set; } = PendingUploadStatus.Pending;

    /// <summary>Last error message from a failed send attempt.</summary>
    public string? LastErrorMessage { get; set; }
}

/// <summary>What a pending upload carries.</summary>
public enum PendingUploadKind
{
    /// <summary>An AlertIngestRequest.</summary>
    Alert,

    /// <summary>An AttributionComplementRequest for the alert of the same ClientMessageId.</summary>
    Attribution,

    /// <summary>
    /// A GenealogyComplementRequest for the alert of the same ClientMessageId: the chain of
    /// processes behind the write. Appended, never inserted -- the value is stored in the
    /// agent's database and renumbering it would change the meaning of every row already there.
    /// </summary>
    Genealogy,
}

/// <summary>Upload status for pending alert retry queue.</summary>
public enum PendingUploadStatus
{
    /// <summary>Waiting for retry.</summary>
    Pending,
    /// <summary>Currently being sent.</summary>
    Uploading,
    /// <summary>GRID rejected the payload (422) — will not be retried.</summary>
    FailedValidation,
    /// <summary>Successfully uploaded to GRID.</summary>
    Uploaded
}
