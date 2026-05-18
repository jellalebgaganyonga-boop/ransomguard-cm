namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates canary file content in a specific format.
/// </summary>
public interface ICanaryFileGenerator
{
    /// <summary>
    /// The file extension produced by this generator (e.g., ".docx", ".txt").
    /// </summary>
    string Extension { get; }

    /// <summary>
    /// Generates canary file content for the given template.
    /// </summary>
    /// <param name="template">Template name (e.g., "dossier_patient").</param>
    /// <returns>The file content as bytes.</returns>
    byte[] Generate(string template);
}
