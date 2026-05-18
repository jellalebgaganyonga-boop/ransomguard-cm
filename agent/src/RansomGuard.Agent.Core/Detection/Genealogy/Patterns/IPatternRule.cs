namespace RansomGuard.Agent.Core.Detection.Genealogy.Patterns;

/// <summary>
/// A single suspicious process pattern rule mapped to a MITRE ATT and CK technique.
/// </summary>
public interface IPatternRule
{
    /// <summary>Unique pattern identifier.</summary>
    string PatternId { get; }

    /// <summary>MITRE technique ID (e.g., "T1490").</summary>
    string MitreTechniqueId { get; }

    /// <summary>MITRE technique name.</summary>
    string MitreTechniqueName { get; }

    /// <summary>Default severity level.</summary>
    string Severity { get; }

    /// <summary>
    /// Evaluates the pattern against a process tree.
    /// Returns a flag if the pattern matches, null otherwise.
    /// </summary>
    SuspiciousPatternFlag? Evaluate(ProcessTree tree);
}
