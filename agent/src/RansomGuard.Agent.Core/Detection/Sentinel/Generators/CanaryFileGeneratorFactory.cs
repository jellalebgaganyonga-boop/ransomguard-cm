namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Factory that selects the appropriate canary file generator based on file extension.
/// Supports .txt, .docx, .pdf, .jpg, .xlsx to defeat extension-filtering ransomware.
/// </summary>
public static class CanaryFileGeneratorFactory
{
    private static readonly TxtCanaryGenerator TxtGenerator = new();
    private static readonly DocxCanaryGenerator DocxGenerator = new();
    private static readonly PdfCanaryGenerator PdfGenerator = new();
    private static readonly JpgCanaryGenerator JpgGenerator = new();
    private static readonly XlsxCanaryGenerator XlsxGenerator = new();

    /// <summary>
    /// Gets the generator for the specified file extension.
    /// </summary>
    /// <param name="extension">File extension (e.g., ".docx", ".pdf", ".jpg", ".xlsx", ".txt").</param>
    /// <returns>The appropriate generator. Defaults to .txt if extension not recognized.</returns>
    public static ICanaryFileGenerator GetGenerator(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => DocxGenerator,
            ".pdf" => PdfGenerator,
            ".jpg" or ".jpeg" => JpgGenerator,
            ".xlsx" => XlsxGenerator,
            ".txt" => TxtGenerator,
            _ => TxtGenerator
        };
    }
}
