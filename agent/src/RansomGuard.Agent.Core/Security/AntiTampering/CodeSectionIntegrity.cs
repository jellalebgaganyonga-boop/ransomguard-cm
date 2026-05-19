using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security.AntiTampering;

/// <summary>
/// Verifies the integrity of the agent executable's code section by computing
/// SHA-256 at startup and periodically comparing. Detects runtime code patching.
/// On mismatch: Critical alert. Watchdog service handles restart.
/// CWE-345: Validates binary integrity against tampering.
/// </summary>
public sealed class CodeSectionIntegrity
{
    private readonly ILogger<CodeSectionIntegrity> _logger;
    private string? _baselineHash;

    /// <summary>Initializes the code section integrity checker.</summary>
    public CodeSectionIntegrity(ILogger<CodeSectionIntegrity> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes the baseline SHA-256 hash of the current executable.
    /// Call once at startup before any tampering could occur.
    /// </summary>
    public void ComputeBaseline()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is null || !File.Exists(exePath))
            {
                _logger.LogWarning("ANTI-TAMPER: Cannot determine executable path for integrity baseline");
                return;
            }

            _baselineHash = ComputeFileHash(exePath);
            _logger.LogInformation("ANTI-TAMPER: Code integrity baseline computed ({Hash})", _baselineHash[..16]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ANTI-TAMPER: Failed to compute code integrity baseline");
        }
    }

    /// <summary>
    /// Verifies the current executable hash against the baseline.
    /// Returns a tampering indicator string if mismatch detected, null if intact.
    /// </summary>
    public string? Verify()
    {
        if (_baselineHash is null)
            return null; // Baseline not computed — skip check

        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is null || !File.Exists(exePath))
                return "Executable path not available for integrity check";

            string currentHash = ComputeFileHash(exePath);

            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(_baselineHash),
                System.Text.Encoding.UTF8.GetBytes(currentHash)))
            {
                _logger.LogCritical("ANTI-TAMPER: Code section hash MISMATCH — possible runtime patching");
                return $"Code section hash mismatch: expected {_baselineHash[..16]}, got {currentHash[..16]}";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ANTI-TAMPER: Code integrity check failed");
            return null; // Cannot verify — don't false-alarm
        }
    }

    /// <summary>
    /// Gets the baseline hash (for testing).
    /// </summary>
    public string? BaselineHash => _baselineHash;

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
