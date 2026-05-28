using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Persisted record of every IronClad command issued and its outcome.
/// Every event is audit-logged with Ed25519 signature chain.
/// </summary>
public sealed class IronCladEvent
{
    /// <summary>Unique event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Correlation ID of the command sent to the device.</summary>
    public required Guid CommandId { get; init; }

    /// <summary>Action that was requested.</summary>
    public required string Action { get; init; }

    /// <summary>Action parameter (e.g., port number).</summary>
    public required string Parameter { get; init; }

    /// <summary>Justification provided by the operator or automated system.</summary>
    public required string Justification { get; init; }

    /// <summary>Alert ID that triggered this action, if automated.</summary>
    public Guid? SourceAlertId { get; init; }

    /// <summary>UTC timestamp when the command was issued.</summary>
    public required DateTime IssuedAt { get; init; }

    /// <summary>UTC timestamp when the response was received.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Outcome of the command execution.</summary>
    public required string Outcome { get; init; }

    /// <summary>Raw response payload from the device.</summary>
    public string? ResponsePayload { get; set; }

    /// <summary>Error message if the command failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>User or system identity that issued the command.</summary>
    public required string IssuedByUser { get; init; }
}
