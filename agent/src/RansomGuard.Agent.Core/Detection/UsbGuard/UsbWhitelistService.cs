using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>
/// USB whitelist service with privacy-preserving serial hashing.
/// Serial numbers are hashed with SHA-256(serial || agent_salt).
/// Salt is generated at first run and stored via DPAPI.
/// Hash comparison uses CryptographicOperations.FixedTimeEquals (CWE-208).
/// </summary>
public sealed class UsbWhitelistService : IUsbWhitelistService
{
    private readonly AgentDbContext _context;
    private readonly ILogger<UsbWhitelistService> _logger;
    private readonly byte[] _salt;

    /// <summary>
    /// Initializes the whitelist service with a persistent salt for serial hashing.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="salt">DPAPI-protected agent salt (32 bytes).</param>
    public UsbWhitelistService(AgentDbContext context, ILogger<UsbWhitelistService> logger, byte[] salt)
    {
        _context = context;
        _logger = logger;
        _salt = salt;
    }

    /// <inheritdoc />
    public async Task<(bool IsWhitelisted, UsbPolicyLevel? PolicyLevel)> IsWhitelistedAsync(
        string serialNumber, CancellationToken ct = default)
    {
        string hash = ComputeSerialHash(serialNumber);

        // Fetch all active entries and compare with constant-time comparison
        var entries = await _context.Set<UsbWhitelistEntry>()
            .Where(e => e.IsActive)
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            // Check expiry
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
            {
                entry.IsActive = false;
                continue;
            }

            // Constant-time comparison (CWE-208)
            byte[] storedHash = Encoding.UTF8.GetBytes(entry.SerialNumberHash);
            byte[] computedHash = Encoding.UTF8.GetBytes(hash);

            if (CryptographicOperations.FixedTimeEquals(storedHash, computedHash))
            {
                if (entry.PolicyLevel == UsbPolicyLevel.BlockAlways)
                    return (false, UsbPolicyLevel.BlockAlways);

                return (true, entry.PolicyLevel);
            }
        }

        // Save any deactivated expired entries
        await _context.SaveChangesAsync(ct);

        return (false, null);
    }

    /// <inheritdoc />
    public async Task<UsbWhitelistEntry> AddAsync(
        string serialNumber, string description, UsbPolicyLevel level,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        string hash = ComputeSerialHash(serialNumber);

        var entry = new UsbWhitelistEntry
        {
            Id = Guid.NewGuid(),
            SerialNumberHash = hash,
            Description = description,
            AddedByUser = Environment.UserName,
            AddedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
            PolicyLevel = level
        };

        _context.Set<UsbWhitelistEntry>().Add(entry);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("USB GUARD: Whitelisted device — {Description} (policy: {Level})", description, level);
        return entry;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _context.Set<UsbWhitelistEntry>().FindAsync([id], ct);
        if (entry is not null)
        {
            entry.IsActive = false;
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("USB GUARD: Removed whitelist entry {Id}", id);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsbWhitelistEntry>> ListActiveAsync(CancellationToken ct = default)
    {
        return await _context.Set<UsbWhitelistEntry>()
            .Where(e => e.IsActive && (e.ExpiresAt == null || e.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(e => e.AddedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<UsbWhitelistEntry> TemporaryApproveAsync(
        string serialNumber, TimeSpan duration, string justification, CancellationToken ct = default)
    {
        _logger.LogWarning("USB GUARD: Temporary approval — {Justification} (duration: {Duration})", justification, duration);
        return await AddAsync(serialNumber, $"TEMP: {justification}", UsbPolicyLevel.StandardScan,
            DateTime.UtcNow.Add(duration), ct);
    }

    private string ComputeSerialHash(string serialNumber)
    {
        byte[] serialBytes = Encoding.UTF8.GetBytes(serialNumber);
        byte[] payload = new byte[serialBytes.Length + _salt.Length];
        serialBytes.CopyTo(payload, 0);
        _salt.CopyTo(payload, serialBytes.Length);

        byte[] hash = SHA256.HashData(payload);
        return Convert.ToBase64String(hash);
    }
}
