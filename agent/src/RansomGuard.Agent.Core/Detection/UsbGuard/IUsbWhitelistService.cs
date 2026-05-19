using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>
/// Manages the USB device whitelist with privacy-preserving hashed serial numbers.
/// </summary>
public interface IUsbWhitelistService
{
    /// <summary>Checks if a device serial number is whitelisted and returns its policy level.</summary>
    Task<(bool IsWhitelisted, UsbPolicyLevel? PolicyLevel)> IsWhitelistedAsync(string serialNumber, CancellationToken ct = default);

    /// <summary>Adds a device to the whitelist.</summary>
    Task<UsbWhitelistEntry> AddAsync(string serialNumber, string description, UsbPolicyLevel level, DateTime? expiresAt = null, CancellationToken ct = default);

    /// <summary>Removes a whitelist entry by ID.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists all active whitelist entries.</summary>
    Task<IReadOnlyList<UsbWhitelistEntry>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>Creates a temporary approval for emergency use.</summary>
    Task<UsbWhitelistEntry> TemporaryApproveAsync(string serialNumber, TimeSpan duration, string justification, CancellationToken ct = default);
}
