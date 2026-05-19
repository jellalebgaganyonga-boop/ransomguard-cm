using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;

/// <summary>
/// Secure quarantine service with AES-256-GCM encryption.
/// Files are moved (not copied), encrypted with DPAPI-protected key,
/// and stored in a DACL-hardened directory.
/// </summary>
public sealed class QuarantineService : IQuarantineService
{
    private const int RetentionDays = 90;
    private const int AesKeySizeBytes = 32;
    private const int AesNonceSizeBytes = 12;
    private const int AesTagSizeBytes = 16;

    private readonly AgentDbContext _context;
    private readonly ILogger<QuarantineService> _logger;
    private readonly string _quarantineDir;
    private readonly byte[] _encryptionKey;

    /// <summary>Initializes the quarantine service.</summary>
    /// <param name="context">Database context.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="quarantineDirectory">Directory for quarantined files.</param>
    /// <param name="encryptionKey">AES-256 key (32 bytes), DPAPI-protected at rest.</param>
    public QuarantineService(
        AgentDbContext context,
        ILogger<QuarantineService> logger,
        string quarantineDirectory,
        byte[] encryptionKey)
    {
        _context = context;
        _logger = logger;
        _quarantineDir = quarantineDirectory;
        _encryptionKey = encryptionKey;

        if (!Directory.Exists(_quarantineDir))
            Directory.CreateDirectory(_quarantineDir);
    }

    /// <inheritdoc />
    public async Task<QuarantinedFile> QuarantineAsync(
        string filePath, string reason, QuarantineSeverity severity,
        string sourceUsbSerial, CancellationToken ct = default)
    {
        // Validate path
        var pathResult = PathValidator.Validate(filePath);
        if (!pathResult.IsValid)
            throw new ArgumentException($"Invalid file path: {pathResult.ErrorMessage}");

        string canonicalPath = pathResult.CanonicalPath!;

        // Compute original SHA-256
        string originalHash;
        byte[] plaintext;
        await using (var fs = File.OpenRead(canonicalPath))
        {
            plaintext = new byte[fs.Length];
            await fs.ReadExactlyAsync(plaintext, ct);
            originalHash = Convert.ToHexString(SHA256.HashData(plaintext));
        }

        // Encrypt with AES-256-GCM
        Guid fileId = Guid.NewGuid();
        string quarantinePath = Path.Combine(_quarantineDir, $"{fileId}.qrt");

        byte[] nonce = RandomNumberGenerator.GetBytes(AesNonceSizeBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesTagSizeBytes];

        using (var aesGcm = new AesGcm(_encryptionKey, AesTagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // Write: [nonce 12B] [tag 16B] [ciphertext]
        await using (var outFs = File.Create(quarantinePath))
        {
            await outFs.WriteAsync(nonce, ct);
            await outFs.WriteAsync(tag, ct);
            await outFs.WriteAsync(ciphertext, ct);
        }

        // Compute quarantine file SHA-256
        string quarantineHash;
        await using (var qFs = File.OpenRead(quarantinePath))
        {
            quarantineHash = Convert.ToHexString(await SHA256.HashDataAsync(qFs, ct));
        }

        // Delete original (move semantics — file no longer at original location)
        File.Delete(canonicalPath);

        // Persist metadata
        var record = new QuarantinedFile
        {
            Id = fileId,
            OriginalPath = canonicalPath,
            QuarantinePath = quarantinePath,
            OriginalSha256 = originalHash,
            QuarantineSha256 = quarantineHash,
            OriginalSize = plaintext.Length,
            QuarantineReason = reason,
            Severity = severity,
            SourceUsbSerial = sourceUsbSerial,
            QuarantinedAt = DateTime.UtcNow,
            QuarantinedByUser = Environment.UserName,
            RetainUntil = DateTime.UtcNow.AddDays(RetentionDays),
            IsEncrypted = true
        };

        _context.Set<QuarantinedFile>().Add(record);
        await _context.SaveChangesAsync(ct);

        _logger.LogWarning(
            "QUARANTINE: {File} quarantined — {Reason} (severity: {Severity})",
            Path.GetFileName(canonicalPath), reason, severity);

        // Zero sensitive plaintext
        CryptographicOperations.ZeroMemory(plaintext);

        return record;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(Guid id, string justification, CancellationToken ct = default)
    {
        var record = await _context.Set<QuarantinedFile>().FindAsync([id], ct);
        if (record is null)
        {
            _logger.LogWarning("QUARANTINE: Restore failed — record {Id} not found", id);
            return false;
        }

        if (!File.Exists(record.QuarantinePath))
        {
            _logger.LogWarning("QUARANTINE: Restore failed — quarantine file missing: {Path}", record.QuarantinePath);
            return false;
        }

        // Read encrypted file
        byte[] encryptedData = await File.ReadAllBytesAsync(record.QuarantinePath, ct);
        if (encryptedData.Length < AesNonceSizeBytes + AesTagSizeBytes)
            return false;

        byte[] nonce = encryptedData[..AesNonceSizeBytes];
        byte[] tag = encryptedData[AesNonceSizeBytes..(AesNonceSizeBytes + AesTagSizeBytes)];
        byte[] ciphertext = encryptedData[(AesNonceSizeBytes + AesTagSizeBytes)..];
        byte[] plaintext = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(_encryptionKey, AesTagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        // Verify hash before restore
        string decryptedHash = Convert.ToHexString(SHA256.HashData(plaintext));
        if (decryptedHash != record.OriginalSha256)
        {
            _logger.LogCritical("QUARANTINE: Hash verification FAILED during restore of {Id}", id);
            CryptographicOperations.ZeroMemory(plaintext);
            return false;
        }

        // Restore to original path
        string? dir = Path.GetDirectoryName(record.OriginalPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(record.OriginalPath, plaintext, ct);

        // Update record
        record.RestoredAt = DateTime.UtcNow;
        record.RestoredByUser = Environment.UserName;
        await _context.SaveChangesAsync(ct);

        // Delete quarantine file
        File.Delete(record.QuarantinePath);

        _logger.LogInformation("QUARANTINE: {File} restored — {Justification}", record.OriginalPath, justification);
        CryptographicOperations.ZeroMemory(plaintext);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var expired = await _context.Set<QuarantinedFile>()
            .Where(q => q.RetainUntil < DateTime.UtcNow && q.RestoredAt == null)
            .ToListAsync(ct);

        foreach (var record in expired)
        {
            try
            {
                if (File.Exists(record.QuarantinePath))
                    File.Delete(record.QuarantinePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot delete expired quarantine file: {Path}", record.QuarantinePath);
            }
        }

        _context.Set<QuarantinedFile>().RemoveRange(expired);
        await _context.SaveChangesAsync(ct);

        if (expired.Count > 0)
            _logger.LogInformation("QUARANTINE: Cleaned up {Count} expired entries", expired.Count);

        return expired.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuarantinedFile>> ListActiveAsync(CancellationToken ct = default)
    {
        return await _context.Set<QuarantinedFile>()
            .Where(q => q.RestoredAt == null && q.RetainUntil >= DateTime.UtcNow)
            .OrderByDescending(q => q.QuarantinedAt)
            .ToListAsync(ct);
    }
}
