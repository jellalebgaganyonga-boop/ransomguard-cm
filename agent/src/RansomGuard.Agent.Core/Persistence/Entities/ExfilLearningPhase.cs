namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// When EXFIL WATCH started learning what this endpoint's network traffic normally looks like.
/// <para>
/// One row, written the first time the module runs on this machine and never moved afterwards.
/// Until 2026-10-22 the start was <c>DateTime.UtcNow</c> in the tracker's constructor, so every
/// agent restart began the seven days again: an endpoint restarted weekly never left the learning
/// phase, and never raised a volume anomaly (AGT-EXF-001).
/// </para>
/// </summary>
public sealed class ExfilLearningPhase
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>UTC instant the learning phase began on this endpoint. Never rewritten.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>The configured length in days, kept to show what the phase was started with.</summary>
    public required int LearningPhaseDays { get; init; }

    /// <summary>UTC instant the row was written.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
