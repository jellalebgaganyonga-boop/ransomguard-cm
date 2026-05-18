namespace RansomGuard.Agent.Core.Detection.Genealogy.Patterns;

/// <summary>
/// T1218: Detects explorer.exe spawning living-off-the-land binaries
/// (wscript, mshta, regsvr32, rundll32) used as signed binary proxies.
/// </summary>
public sealed class SignedBinaryProxyRule : IPatternRule
{
    private static readonly HashSet<string> ProxyBinaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "wscript", "mshta", "regsvr32", "rundll32"
    };

    /// <inheritdoc />
    public string PatternId => "PATTERN_T1218_SIGNED_BINARY_PROXY";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1218";

    /// <inheritdoc />
    public string MitreTechniqueName => "System Binary Proxy Execution";

    /// <inheritdoc />
    public string Severity => "Medium";

    /// <inheritdoc />
    public SuspiciousPatternFlag? Evaluate(ProcessTree tree)
    {
        if (tree.Ancestors.Count == 0) return null;

        ProcessSnapshot parent = tree.Ancestors[0];
        if (!string.Equals(parent.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ProxyBinaries.Contains(tree.Root.ProcessName))
        {
            return new SuspiciousPatternFlag
            {
                TechniqueId = MitreTechniqueId,
                TechniqueName = MitreTechniqueName,
                Description = $"Explorer spawned signed binary proxy: {tree.Root.ProcessName}",
                Severity = Severity
            };
        }

        return null;
    }
}
