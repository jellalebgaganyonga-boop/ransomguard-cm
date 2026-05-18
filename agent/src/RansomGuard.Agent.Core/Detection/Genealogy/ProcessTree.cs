namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Reconstructed process tree showing the chain of process creation.
/// Root is the suspected offending process; Ancestors trace back to the shell.
/// </summary>
public sealed record ProcessTree
{
    /// <summary>The process under investigation.</summary>
    public required ProcessSnapshot Root { get; init; }

    /// <summary>Ancestor chain (parent, grandparent, ...). First element is direct parent.</summary>
    public required IReadOnlyList<ProcessSnapshot> Ancestors { get; init; }

    /// <summary>Depth of the ancestor chain.</summary>
    public int Depth => Ancestors.Count;

    /// <summary>Human-readable summary of the process chain.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            foreach (var a in Ancestors.Reverse())
            {
                parts.Add(a.ProcessName);
            }
            parts.Add(Root.ProcessName);
            return string.Join(" -> ", parts);
        }
    }
}
