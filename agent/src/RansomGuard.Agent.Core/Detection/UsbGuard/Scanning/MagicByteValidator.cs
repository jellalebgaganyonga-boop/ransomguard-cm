using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Security;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Validates file content against declared extension using magic byte signatures.
/// Bounded reads prevent CWE-119 buffer overflow.
/// </summary>
public sealed class MagicByteValidator : IMagicByteValidator
{
    private const int MaxHeaderSize = 16;
    private readonly ILogger<MagicByteValidator> _logger;

    /// <summary>Initializes the magic byte validator.</summary>
    public MagicByteValidator(ILogger<MagicByteValidator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MagicByteValidationResult> ValidateAsync(string filePath, CancellationToken ct = default)
    {
        var pathResult = PathValidator.Validate(filePath);
        if (!pathResult.IsValid)
        {
            return new MagicByteValidationResult
            {
                IsMatch = false,
                DetectedFormat = null,
                DeclaredExtension = Path.GetExtension(filePath),
                Severity = ScanSeverity.High
            };
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            var header = new byte[MaxHeaderSize];
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            int bytesRead = await stream.ReadAsync(header.AsMemory(0, MaxHeaderSize), ct);

            return ValidateBytes(header.AsSpan(0, bytesRead), extension);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot read magic bytes from {FilePath}", filePath);
            return new MagicByteValidationResult
            {
                IsMatch = true, // Cannot determine — assume ok
                DetectedFormat = null,
                DeclaredExtension = extension,
                Severity = ScanSeverity.None
            };
        }
    }

    /// <inheritdoc />
    public MagicByteValidationResult ValidateBytes(ReadOnlySpan<byte> header, string declaredExtension)
    {
        if (header.Length < 2)
        {
            return new MagicByteValidationResult
            {
                IsMatch = true,
                DetectedFormat = null,
                DeclaredExtension = declaredExtension,
                Severity = ScanSeverity.None
            };
        }

        string ext = declaredExtension.ToLowerInvariant();

        // Check if actual content is a PE executable
        bool isPe = header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A;

        // If declared as non-executable but content is PE → Critical
        if (isPe && !MagicByteSignatures.ExecutableExtensions.Contains(ext))
        {
            return new MagicByteValidationResult
            {
                IsMatch = false,
                DetectedFormat = "PE Executable (MZ)",
                DeclaredExtension = ext,
                Severity = ScanSeverity.Critical
            };
        }

        // Check declared extension against its expected signatures
        if (MagicByteSignatures.Signatures.TryGetValue(ext, out byte[][]? expectedSigs))
        {
            // Script formats with no fixed signature → always match
            if (expectedSigs.Length == 0)
            {
                return new MagicByteValidationResult
                {
                    IsMatch = true,
                    DetectedFormat = ext,
                    DeclaredExtension = ext,
                    Severity = ScanSeverity.None
                };
            }

            // Check if any expected signature matches
            foreach (byte[] sig in expectedSigs)
            {
                if (header.Length >= sig.Length && header[..sig.Length].SequenceEqual(sig))
                {
                    return new MagicByteValidationResult
                    {
                        IsMatch = true,
                        DetectedFormat = ext,
                        DeclaredExtension = ext,
                        Severity = ScanSeverity.None
                    };
                }
            }

            // Declared extension has known signatures but none match
            string? detected = DetectActualFormat(header);
            return new MagicByteValidationResult
            {
                IsMatch = false,
                DetectedFormat = detected,
                DeclaredExtension = ext,
                Severity = detected is not null && MagicByteSignatures.ExecutableExtensions.Contains(detected)
                    ? ScanSeverity.Critical
                    : ScanSeverity.High
            };
        }

        // Unknown extension — no validation possible
        return new MagicByteValidationResult
        {
            IsMatch = true,
            DetectedFormat = null,
            DeclaredExtension = ext,
            Severity = ScanSeverity.None
        };
    }

    private static string? DetectActualFormat(ReadOnlySpan<byte> header)
    {
        foreach (var (ext, signatures) in MagicByteSignatures.Signatures)
        {
            foreach (byte[] sig in signatures)
            {
                if (sig.Length > 0 && header.Length >= sig.Length && header[..sig.Length].SequenceEqual(sig))
                    return ext;
            }
        }
        return null;
    }
}
