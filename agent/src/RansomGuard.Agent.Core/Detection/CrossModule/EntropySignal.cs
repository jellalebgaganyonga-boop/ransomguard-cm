namespace RansomGuard.Agent.Core.Detection.CrossModule;

/// <summary>
/// Signal emitted by the ENTROPY module when an entropy alert is created.
/// Consumed by EXFIL WATCH Rule 8 (EncryptedExfilCorrelation) for double extortion detection.
/// </summary>
public sealed record EntropySignal : IDetectionSignal
{
    /// <inheritdoc />
    public required Guid SignalId { get; init; }

    /// <inheritdoc />
    public required string SourceModule { get; init; }

    /// <inheritdoc />
    public required DateTime EmittedAt { get; init; }

    /// <summary>File path that triggered the entropy alert.</summary>
    public required string FilePath { get; init; }

    /// <summary>Process ID that modified the file (0 if unknown).</summary>
    public required int ProcessId { get; init; }

    /// <summary>Process name that modified the file (empty if unknown).</summary>
    public required string ProcessName { get; init; }

    /// <summary>Current Shannon entropy value (bits/byte).</summary>
    public required double EntropyValue { get; init; }

    /// <summary>File size in bytes.</summary>
    public required long FileSize { get; init; }

    /// <summary>Cross-link to the persisted EntropyAlert record.</summary>
    public required Guid EntropyAlertId { get; init; }
}
