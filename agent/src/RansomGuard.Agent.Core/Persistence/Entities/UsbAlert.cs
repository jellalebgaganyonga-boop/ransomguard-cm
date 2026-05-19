namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Alert generated from USB GUARD module, cross-linked with scan results and main alert table.
/// </summary>
public sealed class UsbAlert
{
    /// <summary>Unique alert identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The scan result that triggered this alert.</summary>
    public required Guid UsbScanResultId { get; init; }

    /// <summary>Cross-link to the main Alert table.</summary>
    public Guid? AlertId { get; init; }

    /// <summary>Alert title describing the finding.</summary>
    public required string Title { get; init; }

    /// <summary>Detailed description of the USB threat.</summary>
    public required string Description { get; init; }

    /// <summary>Alert severity.</summary>
    public required string Severity { get; init; }

    /// <summary>Action taken in response (AlertOnly, Quarantine, Eject, etc.).</summary>
    public required string ActionTaken { get; init; }

    /// <summary>UTC timestamp when the alert was generated.</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Cross-link to GenealogyRecord for process attribution.</summary>
    public Guid? GenealogyId { get; init; }
}
