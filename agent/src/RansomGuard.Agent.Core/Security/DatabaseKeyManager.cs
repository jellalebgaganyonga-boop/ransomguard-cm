using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Manages the SQLCipher encryption key for the agent SQLite database (CWE-311, CWE-312).
/// Key is generated on first run and stored encrypted via Windows DPAPI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DatabaseKeyManager
{
    private const int KeySizeBytes = 32; // 256-bit AES key
    private readonly string _keyFilePath;
    private readonly ILogger<DatabaseKeyManager> _logger;

    /// <summary>
    /// Initializes the key manager with the key file path.
    /// </summary>
    /// <param name="keyDirectory">Directory to store the DPAPI-protected key file.</param>
    /// <param name="logger">Logger instance.</param>
    public DatabaseKeyManager(string keyDirectory, ILogger<DatabaseKeyManager> logger)
    {
        _keyFilePath = Path.Combine(keyDirectory, "db.key");
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates the database encryption key.
    /// On first run, generates a new 256-bit key and stores it via DPAPI.
    /// On subsequent runs, reads and decrypts the stored key.
    /// </summary>
    /// <returns>The raw encryption key as a hex string for SQLCipher.</returns>
    public string GetOrCreateKey()
    {
        string? keyDir = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(keyDir) && !Directory.Exists(keyDir))
        {
            Directory.CreateDirectory(keyDir);
            _logger.LogInformation("Created key directory: {Directory}", keyDir);
        }

        if (File.Exists(_keyFilePath))
        {
            return LoadKey();
        }

        return GenerateAndStoreKey();
    }

    private string GenerateAndStoreKey()
    {
        byte[] key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        string hexKey = Convert.ToHexString(key).ToLowerInvariant();

        // Protect with DPAPI (machine-level scope so the service account can read it)
        byte[] protectedKey = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(hexKey),
            null,
            DataProtectionScope.LocalMachine);

        File.WriteAllBytes(_keyFilePath, protectedKey);

        _logger.LogInformation("Database encryption key generated and stored at {Path}", _keyFilePath);
        return hexKey;
    }

    private string LoadKey()
    {
        byte[] protectedKey = File.ReadAllBytes(_keyFilePath);

        byte[] decryptedBytes = ProtectedData.Unprotect(
            protectedKey,
            null,
            DataProtectionScope.LocalMachine);

        string hexKey = System.Text.Encoding.UTF8.GetString(decryptedBytes);

        _logger.LogDebug("Database encryption key loaded from {Path}", _keyFilePath);
        return hexKey;
    }
}
