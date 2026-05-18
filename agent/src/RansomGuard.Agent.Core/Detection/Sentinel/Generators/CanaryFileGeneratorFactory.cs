namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Factory that selects the appropriate canary file generator based on file extension.
/// </summary>
public static class CanaryFileGeneratorFactory
{
    private static readonly TxtCanaryGenerator TxtGenerator = new();
    private static readonly DocxCanaryGenerator DocxGenerator = new();

    /// <summary>
    /// Gets the generator for the specified file extension.
    /// </summary>
    /// <param name="extension">File extension (e.g., ".docx", ".txt").</param>
    /// <returns>The appropriate generator. Defaults to .txt if extension not recognized.</returns>
    public static ICanaryFileGenerator GetGenerator(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => DocxGenerator,
            ".txt" => TxtGenerator,
            _ => TxtGenerator
        };
    }
}
