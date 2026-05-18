using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;

/// <summary>
/// A single entropy detection rule evaluated against file entropy data.
/// </summary>
public interface IEntropyRule
{
    /// <summary>Rule identifier (1-4).</summary>
    int RuleId { get; }

    /// <summary>Human-readable rule name.</summary>
    string Name { get; }

    /// <summary>
    /// Evaluates the rule against the given context.
    /// Returns an alert if the rule fires, null otherwise.
    /// </summary>
    EntropyAlert? Evaluate(EntropyEvaluationContext context);
}

/// <summary>
/// Context passed to each entropy detection rule for evaluation.
/// </summary>
public sealed record EntropyEvaluationContext
{
    /// <summary>Full path of the file under analysis.</summary>
    public required string FilePath { get; init; }

    /// <summary>File extension (lowercase, e.g., ".docx").</summary>
    public required string FileExtension { get; init; }

    /// <summary>Current entropy measurement (bits/byte).</summary>
    public required double CurrentEntropy { get; init; }

    /// <summary>Current file size in bytes.</summary>
    public required long CurrentSize { get; init; }

    /// <summary>Previous baseline measurement, or null if no baseline exists.</summary>
    public required EntropyBaseline? Baseline { get; init; }

    /// <summary>Number of entropy alerts in the same directory within the time window.</summary>
    public required int RecentDirectoryAlertCount { get; init; }

    /// <summary>Average entropy delta across recent alerts in the directory.</summary>
    public required double AverageDirectoryDelta { get; init; }
}
