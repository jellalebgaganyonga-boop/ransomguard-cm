namespace RansomGuard.Agent.Core.Detection.Genealogy.Patterns;

/// <summary>
/// T1566: Detects Outlook spawning an executable outside of Program Files / Windows.
/// Indicates a phishing attachment was opened directly from email.
/// </summary>
public sealed class EmailAttachmentRule : IPatternRule
{
    /// <inheritdoc />
    public string PatternId => "PATTERN_T1566_EMAIL_ATTACHMENT";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1566";

    /// <inheritdoc />
    public string MitreTechniqueName => "Phishing: Attachment";

    /// <inheritdoc />
    public string Severity => "High";

    /// <inheritdoc />
    public SuspiciousPatternFlag? Evaluate(ProcessTree tree)
    {
        if (tree.Ancestors.Count == 0) return null;

        ProcessSnapshot parent = tree.Ancestors[0];
        if (!string.Equals(parent.ProcessName, "outlook", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parent.ProcessName, "OUTLOOK", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? childPath = tree.Root.ExecutablePath?.ToLowerInvariant();
        if (childPath is null) return null;

        // Flag if the child exe is NOT in a trusted location
        if (!childPath.StartsWith(@"c:\program files") && !childPath.StartsWith(@"c:\windows"))
        {
            return new SuspiciousPatternFlag
            {
                TechniqueId = MitreTechniqueId,
                TechniqueName = MitreTechniqueName,
                Description = $"Outlook spawned {tree.Root.ProcessName} from untrusted path: {childPath}",
                Severity = Severity
            };
        }

        return null;
    }
}
