namespace RansomGuard.Agent.Core.Detection.UsbGuard.Models;

/// <summary>
/// Represents a USB device detected by the system.
/// Captures hardware identifiers, classification, and connection state.
/// </summary>
public sealed record UsbDevice
{
    /// <summary>Unique device tracking identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Windows device instance ID (e.g., USB\VID_0781&amp;PID_5567\serial).</summary>
    public required string DeviceInstanceId { get; init; }

    /// <summary>Device serial number (from USB descriptor).</summary>
    public required string SerialNumber { get; init; }

    /// <summary>USB Vendor ID (4 hex chars).</summary>
    public required string VendorId { get; init; }

    /// <summary>USB Product ID (4 hex chars).</summary>
    public required string ProductId { get; init; }

    /// <summary>Device manufacturer string.</summary>
    public required string Manufacturer { get; init; }

    /// <summary>Product description string.</summary>
    public required string ProductDescription { get; init; }

    /// <summary>Device model name.</summary>
    public required string Model { get; init; }

    /// <summary>Assigned drive letter (e.g., "E:"), null if not mass storage.</summary>
    public required string? DriveLetter { get; init; }

    /// <summary>Storage capacity in bytes, null if not mass storage.</summary>
    public required long? CapacityBytes { get; init; }

    /// <summary>USB device class classification.</summary>
    public required UsbDeviceClass DeviceClass { get; init; }

    /// <summary>USB bus type (2.0, 3.0, etc.).</summary>
    public required UsbBusType BusType { get; init; }

    /// <summary>Whether the device is removable media.</summary>
    public required bool IsRemovable { get; init; }

    /// <summary>Whether the device contains boot signatures (MBR/GPT).</summary>
    public required bool IsBootable { get; init; }

    /// <summary>UTC timestamp when the device was first detected.</summary>
    public required DateTime ConnectedAt { get; init; }

    /// <summary>UTC timestamp when the device was removed, null if still connected.</summary>
    public DateTime? DisconnectedAt { get; set; }
}
