using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace RansomGuard.Agent.Core.Security.Cryptography;

/// <summary>
/// Signs and verifies audit log entries using Ed25519 for cryptographic non-repudiation.
/// Private key is stored via Windows DPAPI. Public key is exported for external auditors.
/// Compliant with ANTIC audit trail requirements and Cameroon Law 2024/017.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuditLogSigner
{
    private readonly Key _signingKey;
    private readonly PublicKey _publicKey = null!;
    private readonly ILogger<AuditLogSigner> _logger;

    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    /// <summary>
    /// Initializes the signer, loading or generating the Ed25519 keypair.
    /// </summary>
    /// <param name="keyDirectory">Directory for key storage.</param>
    /// <param name="logger">Logger instance.</param>
    public AuditLogSigner(string keyDirectory, ILogger<AuditLogSigner> logger)
    {
        _logger = logger;

        if (!Directory.Exists(keyDirectory))
        {
            Directory.CreateDirectory(keyDirectory);
        }

        string privateKeyPath = Path.Combine(keyDirectory, "audit.key");
        string publicKeyPath = Path.Combine(keyDirectory, "audit.pub");

        if (File.Exists(privateKeyPath))
        {
            _signingKey = LoadPrivateKey(privateKeyPath);
            _logger.LogDebug("Ed25519 audit signing key loaded from {Path}", privateKeyPath);
        }
        else
        {
            _signingKey = Key.Create(Algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            SavePrivateKey(privateKeyPath, _signingKey);
            ExportPublicKey(publicKeyPath, _signingKey.PublicKey);
            _logger.LogInformation("Ed25519 audit signing keypair generated. Public key at {Path}", publicKeyPath);
        }

        _publicKey = _signingKey.PublicKey;

        // Always ensure public key is exported
        if (!File.Exists(publicKeyPath))
        {
            ExportPublicKey(publicKeyPath, _signingKey.PublicKey);
        }
    }

    /// <summary>
    /// Signs the canonical audit log payload.
    /// </summary>
    /// <param name="id">Audit entry ID.</param>
    /// <param name="timestamp">Entry timestamp.</param>
    /// <param name="action">Action type.</param>
    /// <param name="previousHash">Previous entry hash.</param>
    /// <param name="currentHash">Current entry hash.</param>
    /// <returns>Base64-encoded Ed25519 signature (88 chars).</returns>
    public string Sign(Guid id, DateTime timestamp, string action, string? previousHash, string currentHash)
    {
        byte[] payload = BuildCanonicalPayload(id, timestamp, action, previousHash, currentHash);
        byte[] signature = Algorithm.Sign(_signingKey, payload);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Verifies an Ed25519 signature against the canonical audit log payload.
    /// Uses timing-safe comparison internally (CWE-208).
    /// </summary>
    /// <returns>True if signature is valid.</returns>
    public bool Verify(Guid id, DateTime timestamp, string action, string? previousHash, string currentHash, string signatureBase64)
    {
        byte[] payload = BuildCanonicalPayload(id, timestamp, action, previousHash, currentHash);
        byte[] signature;

        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return Algorithm.Verify(_publicKey, payload, signature);
    }

    /// <summary>
    /// Gets the public key as a Base64 string for external auditor distribution.
    /// </summary>
    public string GetPublicKeyBase64()
    {
        byte[] publicKeyBytes = _publicKey.Export(KeyBlobFormat.RawPublicKey);
        return Convert.ToBase64String(publicKeyBytes);
    }

    private static byte[] BuildCanonicalPayload(Guid id, DateTime timestamp, string action, string? previousHash, string currentHash)
    {
        // Canonical byte sequence: Id || Timestamp (ticks BE) || Action (UTF8) || PreviousHash || CurrentHash
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(id.ToByteArray());
        writer.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(timestamp.Ticks));
        byte[] actionBytes = Encoding.UTF8.GetBytes(action);
        writer.Write(actionBytes.Length);
        writer.Write(actionBytes);
        byte[] prevBytes = Encoding.UTF8.GetBytes(previousHash ?? "GENESIS");
        writer.Write(prevBytes.Length);
        writer.Write(prevBytes);
        byte[] currBytes = Encoding.UTF8.GetBytes(currentHash);
        writer.Write(currBytes.Length);
        writer.Write(currBytes);

        writer.Flush();
        return ms.ToArray();
    }

    private void SavePrivateKey(string path, Key key)
    {
        byte[] rawKey = key.Export(KeyBlobFormat.RawPrivateKey);
        byte[] protectedKey = ProtectedData.Protect(rawKey, null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(path, protectedKey);
        CryptographicOperations.ZeroMemory(rawKey);
    }

    private static Key LoadPrivateKey(string path)
    {
        byte[] protectedKey = File.ReadAllBytes(path);
        byte[] rawKey = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.LocalMachine);

        try
        {
            return Key.Import(Algorithm, rawKey, KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
    }

    private static void ExportPublicKey(string path, PublicKey publicKey)
    {
        byte[] publicKeyBytes = publicKey.Export(KeyBlobFormat.RawPublicKey);
        string base64 = Convert.ToBase64String(publicKeyBytes);
        File.WriteAllText(path, base64);
    }
}
