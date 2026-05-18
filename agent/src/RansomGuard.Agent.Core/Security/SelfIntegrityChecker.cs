using System.Reflection;
using System.Security.Cryptography;

namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Verifies the integrity of the agent binary at startup by comparing its
/// SHA-256 hash against a pre-computed hash file. Anti-tampering baseline
/// (preparation for Sprint 8 Authenticode signing).
/// </summary>
public static class SelfIntegrityChecker
{
    private const string HashFileExtension = ".sha256";

    /// <summary>
    /// Verifies the executing assembly's integrity against a companion .sha256 file.
    /// </summary>
    /// <returns>Verification result with pass/fail and reason.</returns>
    public static IntegrityResult Verify()
    {
        string? assemblyLocation = Assembly.GetEntryAssembly()?.Location;

        if (string.IsNullOrEmpty(assemblyLocation))
        {
            return IntegrityResult.Skip("Cannot determine assembly location (single-file publish or dynamic context)");
        }

        string hashFilePath = assemblyLocation + HashFileExtension;

        if (!File.Exists(hashFilePath))
        {
            return IntegrityResult.Skip("Hash file not found — development build (no integrity check)");
        }

        string expectedHash = File.ReadAllText(hashFilePath).Trim().ToUpperInvariant();

        byte[] fileBytes = File.ReadAllBytes(assemblyLocation);
        string actualHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        if (CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expectedHash),
            System.Text.Encoding.UTF8.GetBytes(actualHash)))
        {
            return IntegrityResult.Pass(actualHash);
        }

        return IntegrityResult.Fail(
            $"Binary hash mismatch. Expected: {expectedHash[..16]}... Actual: {actualHash[..16]}...");
    }

    /// <summary>
    /// Computes and saves the SHA-256 hash of the specified file.
    /// Used by build scripts to generate the companion hash file.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    public static void GenerateHashFile(string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        string hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        File.WriteAllText(filePath + HashFileExtension, hash);
    }
}

/// <summary>
/// Result of the self-integrity verification.
/// </summary>
public sealed record IntegrityResult
{
    /// <summary>
    /// Whether the check passed, failed, or was skipped.
    /// </summary>
    public required IntegrityStatus Status { get; init; }

    /// <summary>
    /// Human-readable reason.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The computed hash if verification succeeded.
    /// </summary>
    public string? Hash { get; init; }

    /// <summary>Creates a pass result.</summary>
    public static IntegrityResult Pass(string hash) => new()
    {
        Status = IntegrityStatus.Passed,
        Reason = "Binary integrity verified",
        Hash = hash
    };

    /// <summary>Creates a fail result.</summary>
    public static IntegrityResult Fail(string reason) => new()
    {
        Status = IntegrityStatus.Failed,
        Reason = reason
    };

    /// <summary>Creates a skip result (development builds).</summary>
    public static IntegrityResult Skip(string reason) => new()
    {
        Status = IntegrityStatus.Skipped,
        Reason = reason
    };
}

/// <summary>
/// Status of self-integrity check.
/// </summary>
public enum IntegrityStatus
{
    /// <summary>Hash matches — binary is intact.</summary>
    Passed,
    /// <summary>Hash mismatch — binary may be tampered.</summary>
    Failed,
    /// <summary>Check skipped (dev build, no hash file).</summary>
    Skipped
}
