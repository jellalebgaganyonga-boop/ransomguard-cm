namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Alert generated when entropy analysis detects likely encryption activity.
/// Contains forensic evidence: baseline vs current entropy, rule triggered, file details.
/// </summary>
public sealed class EntropyAlert
{
    /// <summary>Unique alert identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>File path that triggered the alert.</summary>
    public required string FilePath { get; init; }

    /// <summary>Detection rule that fired (1=AbsoluteHigh, 2=Delta, 3=DirectoryShift).</summary>
    public required int RuleId { get; init; }

    /// <summary>Human-readable rule name.</summary>
    public required string RuleName { get; init; }

    /// <summary>Alert severity.</summary>
    public required string Severity { get; init; }

    /// <summary>Baseline entropy value (bits/byte) before the change.</summary>
    public required double BaselineEntropy { get; init; }

    /// <summary>Current entropy value (bits/byte) triggering the alert.</summary>
    public required double CurrentEntropy { get; init; }

    /// <summary>Entropy delta (CurrentEntropy - BaselineEntropy).</summary>
    public required double Delta { get; init; }

    /// <summary>UTC timestamp when the alert was detected.</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
