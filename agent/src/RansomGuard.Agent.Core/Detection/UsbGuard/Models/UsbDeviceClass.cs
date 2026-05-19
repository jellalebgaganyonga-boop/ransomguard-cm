namespace RansomGuard.Agent.Core.Detection.UsbGuard.Models;

/// <summary>
/// USB device class classification based on USB Interface Class codes.
/// </summary>
public enum UsbDeviceClass
{
    /// <summary>Mass storage device (flash drive, external HDD/SSD).</summary>
    MassStorage,
    /// <summary>Human Interface Device (keyboard, mouse, gamepad).</summary>
    Hid,
    /// <summary>Printer device.</summary>
    Printer,
    /// <summary>Audio device (headset, speakers, microphone).</summary>
    Audio,
    /// <summary>Video device (webcam, capture card).</summary>
    Video,
    /// <summary>Network adapter (WiFi dongle, 4G modem, Bluetooth).</summary>
    Network,
    /// <summary>Smart card reader.</summary>
    SmartCardReader,
    /// <summary>Barcode scanner.</summary>
    BarcodeScanner,
    /// <summary>Composite device with multiple interfaces.</summary>
    Composite,
    /// <summary>Unknown or unclassified device.</summary>
    Unknown
}

/// <summary>
/// USB bus type indicating the physical connection standard.
/// </summary>
public enum UsbBusType
{
    /// <summary>USB 2.0 (480 Mbps).</summary>
    Usb20,
    /// <summary>USB 3.0 (5 Gbps).</summary>
    Usb30,
    /// <summary>USB 3.1 (10 Gbps).</summary>
    Usb31,
    /// <summary>USB 3.2 (20 Gbps).</summary>
    Usb32,
    /// <summary>Unknown bus type.</summary>
    Unknown
}
