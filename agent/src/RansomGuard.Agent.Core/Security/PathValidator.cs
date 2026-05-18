namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Validates file system paths to prevent directory traversal attacks (CWE-22, CWE-23, CWE-73).
/// Used at configuration validation (startup) and runtime (canary creation).
/// </summary>
public static class PathValidator
{
    private static readonly char[] DangerousChars = ['<', '>', '|', '?', '*', '"'];

    private static readonly string[] ReservedWindowsNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Validates a file system path for safety. Returns the canonical path if valid.
    /// </summary>
    /// <param name="path">Path to validate.</param>
    /// <returns>Validation result with canonical path or error message.</returns>
    public static PathValidationResult Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathValidationResult.Invalid("Path cannot be empty");
        }

        // Expand environment variables first
        string expanded = Environment.ExpandEnvironmentVariables(path);

        // Detect traversal sequences
        if (expanded.Contains(".."))
        {
            return PathValidationResult.Invalid("Path contains traversal sequence '..'");
        }

        // Check for dangerous characters
        if (expanded.IndexOfAny(DangerousChars) >= 0)
        {
            return PathValidationResult.Invalid("Path contains illegal characters");
        }

        // Check for reserved Windows names BEFORE GetFullPath (Windows resolves CON to \\.\CON)
        string? lastSegment = Path.GetFileName(expanded);
        string? nameWithoutExt = Path.GetFileNameWithoutExtension(expanded);
        if (nameWithoutExt is not null && IsReservedWindowsName(nameWithoutExt))
        {
            return PathValidationResult.Invalid($"Reserved Windows name: {nameWithoutExt}");
        }

        // Resolve to canonical absolute path
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            return PathValidationResult.Invalid($"Invalid path syntax: {ex.Message}");
        }

        // Block UNC paths by default (CWE-23)
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return PathValidationResult.Invalid("UNC paths are not allowed");
        }

        return PathValidationResult.Valid(fullPath);
    }

    private static bool IsReservedWindowsName(string name)
    {
        return Array.Exists(ReservedWindowsNames,
            reserved => string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Result of a path validation operation.
/// </summary>
public sealed record PathValidationResult
{
    /// <summary>
    /// Whether the path is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The canonical path, if valid.
    /// </summary>
    public string? CanonicalPath { get; init; }

    /// <summary>
    /// Error message, if invalid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a valid result with the canonical path.
    /// </summary>
    public static PathValidationResult Valid(string canonicalPath) => new()
    {
        IsValid = true,
        CanonicalPath = canonicalPath
    };

    /// <summary>
    /// Creates an invalid result with an error message.
    /// </summary>
    public static PathValidationResult Invalid(string error) => new()
    {
        IsValid = false,
        ErrorMessage = error
    };
}
