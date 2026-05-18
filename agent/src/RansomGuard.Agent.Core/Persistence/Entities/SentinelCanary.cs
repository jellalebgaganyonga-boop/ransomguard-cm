namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Represents a deployed SENTINEL canary file tracked in the database.
/// Canary files are semantic medical decoys that detect ransomware by being touched first.
/// </summary>
public sealed class SentinelCanary
{
    /// <summary>
    /// Unique canary identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Full path to the canary file on disk.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Canary file name (without directory).
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Directory containing the canary file.
    /// </summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Template used to generate canary content (e.g., "dossier_patient").
    /// </summary>
    public required string TemplateUsed { get; init; }

    /// <summary>
    /// SHA-256 hash of the original canary file content at creation time.
    /// </summary>
    public required string OriginalContentHash { get; set; }

    /// <summary>
    /// File size in bytes at creation time.
    /// </summary>
    public required long FileSize { get; set; }

    /// <summary>
    /// Current canary status.
    /// </summary>
    public CanaryStatus Status { get; set; } = CanaryStatus.Active;

    /// <summary>
    /// UTC timestamp when the canary was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the canary was last verified.
    /// </summary>
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a SENTINEL canary file.
/// </summary>
public enum CanaryStatus
{
    /// <summary>File is intact and actively monitored.</summary>
    Active,
    /// <summary>File content was modified by an unauthorized process.</summary>
    Modified,
    /// <summary>File was deleted from disk.</summary>
    Deleted,
    /// <summary>File was renamed on disk.</summary>
    Renamed,
    /// <summary>File was compromised (generic state after alert).</summary>
    Compromised
}
