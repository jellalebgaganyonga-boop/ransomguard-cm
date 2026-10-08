using RansomGuard.Agent.Core.Communication.Models;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// Client for the RansomGuard GRID management server API.
/// Handles enrollment, heartbeat, alert forwarding, and audit log submission.
/// </summary>
public interface IGridApiClient
{
    /// <summary>Whether the agent is currently enrolled with the GRID server.</summary>
    bool IsEnrolled { get; }

    /// <summary>The agent ID assigned by the GRID server after enrollment.</summary>
    string? AgentId { get; }

    /// <summary>The tenant ID assigned by the GRID server after enrollment.</summary>
    string? TenantId { get; }

    /// <summary>
    /// Enroll this agent with the GRID server using a one-time password.
    /// </summary>
    Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Send a heartbeat to the GRID server.
    /// Returns pending command IDs and the next heartbeat interval.
    /// </summary>
    Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);

    /// <summary>
    /// Forward a detection alert to the GRID server.
    /// </summary>
    Task<AlertIngestResponse> SendAlertAsync(AlertIngestRequest request, CancellationToken ct = default);

    /// <summary>
    /// Submit a batch of Ed25519-signed audit log entries.
    /// </summary>
    Task<AuditLogBatchResponse> SendAuditLogBatchAsync(AuditLogBatchRequest request, CancellationToken ct = default);
}
