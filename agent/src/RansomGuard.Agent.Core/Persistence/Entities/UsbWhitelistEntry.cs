namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// A whitelisted USB device identified by its hashed serial number.
/// Serial numbers are hashed with SHA-256 and a DPAPI-protected agent salt
/// for privacy preservation (CWE-345 mitigation).
/// </summary>
public sealed class UsbWhitelistEntry
{
    /// <summary>Unique entry identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>SHA-256(serial || agent_salt) hash of the device serial number.</summary>
    public required string SerialNumberHash { get; init; }

    /// <summary>Human-readable description of the whitelisted device.</summary>
    public required string Description { get; init; }

    /// <summary>User who added the entry.</summary>
    public required string AddedByUser { get; init; }

    /// <summary>UTC timestamp when the entry was added.</summary>
    public required DateTime AddedAt { get; init; }

    /// <summary>Optional expiry for temporary approvals.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Whether the entry is currently active.</summary>
    public required bool IsActive { get; set; }

    /// <summary>Policy level controlling scan behavior for this device.</summary>
    public required UsbPolicyLevel PolicyLevel { get; init; }
}

/// <summary>
/// Policy level for a whitelisted USB device.
/// </summary>
public enum UsbPolicyLevel
{
    /// <summary>Fully trusted — skip scanning.</summary>
    Trusted,
    /// <summary>Standard scan only (magic bytes, autorun).</summary>
    StandardScan,
    /// <summary>Deep scan including archive recursion and entropy.</summary>
    DeepScan,
    /// <summary>Always block regardless of whitelist status.</summary>
    BlockAlways
}
