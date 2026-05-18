using System.Text;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates canary files in plain text format (.txt).
/// </summary>
public sealed class TxtCanaryGenerator : ICanaryFileGenerator
{
    /// <inheritdoc />
    public string Extension => ".txt";

    /// <inheritdoc />
    public byte[] Generate(string template)
    {
        (byte[] content, _) = CanaryContentGenerator.Generate(template);
        return content;
    }
}
