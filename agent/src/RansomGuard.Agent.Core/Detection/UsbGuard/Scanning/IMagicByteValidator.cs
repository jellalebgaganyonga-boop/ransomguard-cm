namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Validates that a file's extension matches its actual content via magic byte analysis.
/// Detects extension mismatch attacks (e.g., PE executable disguised as .pdf).
/// </summary>
public interface IMagicByteValidator
{
    /// <summary>
    /// Validates file content against its declared extension.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with detected format and severity.</returns>
    Task<MagicByteValidationResult> ValidateAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Validates raw bytes against a declared extension (for unit testing).
    /// </summary>
    MagicByteValidationResult ValidateBytes(ReadOnlySpan<byte> header, string declaredExtension);
}

/// <summary>
/// Result of magic byte validation.
/// </summary>
public sealed record MagicByteValidationResult
{
    /// <summary>Whether the declared extension matches the actual content.</summary>
    public required bool IsMatch { get; init; }

    /// <summary>Detected file format based on magic bytes, null if unknown.</summary>
    public required string? DetectedFormat { get; init; }

    /// <summary>The file extension declared by the filename.</summary>
    public required string DeclaredExtension { get; init; }

    /// <summary>Severity of mismatch: Low (benign), High (suspicious), Critical (executable disguised).</summary>
    public required ScanSeverity Severity { get; init; }
}

/// <summary>
/// Severity level for USB scan findings.
/// </summary>
public enum ScanSeverity
{
    /// <summary>No issue or informational.</summary>
    None,
    /// <summary>Minor mismatch, likely benign.</summary>
    Low,
    /// <summary>Suspicious mismatch warranting review.</summary>
    Medium,
    /// <summary>Likely malicious (e.g., archive with executable).</summary>
    High,
    /// <summary>Confirmed malicious pattern (e.g., PE disguised as document).</summary>
    Critical
}
