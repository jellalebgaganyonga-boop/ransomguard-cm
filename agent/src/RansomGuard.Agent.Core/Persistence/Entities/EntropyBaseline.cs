namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Stores per-file entropy baseline for delta-based ransomware detection.
/// Baseline is built at startup and periodically refreshed.
/// </summary>
public sealed class EntropyBaseline
{
    /// <summary>Unique record identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Full file path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Parent directory for aggregation queries.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>File extension (lowercase, e.g., ".docx").</summary>
    public required string FileExtension { get; init; }

    /// <summary>Shannon entropy at baseline time (bits/byte).</summary>
    public required double EntropyValue { get; init; }

    /// <summary>File size at baseline time.</summary>
    public required long FileSize { get; init; }

    /// <summary>UTC timestamp when baseline was captured.</summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when last verified as unchanged.</summary>
    public DateTime? LastVerifiedAt { get; set; }
}
