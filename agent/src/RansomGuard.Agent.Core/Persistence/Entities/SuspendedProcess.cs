namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// A process SENTINEL suspended after it wrote a canary (ADR-026). Persisted because the suspension
/// outlives the agent process: a suspended process stays suspended until someone resumes it or the
/// machine restarts, and the console must keep seeing it.
/// </summary>
public sealed class SuspendedProcess
{
    /// <summary>The suspension id; the console names it to ask for a resume.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The PID, meaningful only with <see cref="ProcessCreatedAt"/> (ADR-025, 4b).</summary>
    public required int ProcessId { get; init; }

    /// <summary>The creation time of the suspended process (UTC): the other half of its identity.</summary>
    public required DateTime ProcessCreatedAt { get; init; }

    /// <summary>The process name, when known.</summary>
    public string? ProcessName { get; init; }

    /// <summary>The executable path, when known.</summary>
    public string? ImagePath { get; init; }

    /// <summary>The canary it wrote.</summary>
    public required string CanaryPath { get; init; }

    /// <summary>The local canary alert that led here.</summary>
    public required Guid AlertId { get; init; }

    /// <summary>The GRID message id of that alert: the console links the response to it.</summary>
    public required string ClientMessageId { get; init; }

    /// <summary>When the process was suspended (UTC).</summary>
    public required DateTime SuspendedAt { get; init; }

    /// <summary>Where the suspension stands.</summary>
    public SuspensionState State { get; set; } = SuspensionState.Suspended;

    /// <summary>When it was resumed, or found gone (UTC).</summary>
    public DateTime? ResumedAt { get; set; }

    /// <summary>Who resumed it: a console command id, or the operator's reason.</summary>
    public string? ResumedBy { get; set; }

    /// <summary>When the last "no decision" escalation alert was raised.</summary>
    public DateTime? LastEscalationAt { get; set; }

    /// <summary>How many escalation alerts were raised.</summary>
    public int EscalationCount { get; set; }

    /// <summary>The last kernel error, when a resume failed.</summary>
    public string? LastError { get; set; }
}

/// <summary>Where a suspension stands.</summary>
public enum SuspensionState
{
    /// <summary>Suspended, waiting for a human decision.</summary>
    Suspended,

    /// <summary>Resumed on request.</summary>
    Resumed,

    /// <summary>The process is gone (exited, or the machine restarted).</summary>
    Ended,

    /// <summary>A resume was requested and failed three times: the process stays suspended until the machine restarts.</summary>
    ResumeFailed,
}
