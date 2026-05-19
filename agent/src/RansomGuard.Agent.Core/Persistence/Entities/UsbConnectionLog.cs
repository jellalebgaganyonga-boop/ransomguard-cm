namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Log entry for a USB device connection/disconnection event.
/// 90-day retention per Cameroon Law 2024/017 Article 22.
/// </summary>
public sealed class UsbConnectionLog
{
    /// <summary>Unique log identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Windows device instance ID.</summary>
    public required string DeviceInstanceId { get; init; }

    /// <summary>Hashed serial number (SHA-256 + salt).</summary>
    public required string SerialNumberHash { get; init; }

    /// <summary>USB Vendor ID.</summary>
    public required string VendorId { get; init; }

    /// <summary>USB Product ID.</summary>
    public required string ProductId { get; init; }

    /// <summary>Device class (MassStorage, Hid, etc.).</summary>
    public required string DeviceClass { get; init; }

    /// <summary>Assigned drive letter, null if not mass storage.</summary>
    public string? DriveLetter { get; init; }

    /// <summary>UTC timestamp when the device was connected.</summary>
    public required DateTime ConnectedAt { get; init; }

    /// <summary>UTC timestamp when the device was disconnected.</summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>Whether the device was whitelisted at connection time.</summary>
    public required bool WasWhitelisted { get; init; }

    /// <summary>Retention deadline — 90 days from connection.</summary>
    public required DateTime RetainUntil { get; init; }
}
