using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Actions;

/// <summary>What a device call came to: done, or not, with the system's own error.</summary>
/// <param name="Success">True when the device is now in the requested state.</param>
/// <param name="Error">The Win32 / Configuration Manager error when not; null when done.</param>
public sealed record UsbControlResult(bool Success, string? Error)
{
    /// <summary>The call did what was asked.</summary>
    public static UsbControlResult Done { get; } = new(true, null);

    /// <summary>The call failed; the error says why.</summary>
    public static UsbControlResult Failed(string error) => new(false, error);
}

/// <summary>The two things USB GUARD does to a device (V7): make it read-only, eject it. Replaced in tests.</summary>
public interface IUsbDeviceControl
{
    /// <summary>
    /// Makes the disk behind a drive letter read-only for as long as it stays plugged
    /// (DISK_ATTRIBUTE_READ_ONLY, not persisted): every later write fails with "write protected".
    /// </summary>
    UsbControlResult SetReadOnly(string driveLetter);

    /// <summary>Ejects the device (CM_Request_Device_Eject): the volume is dismounted and the device removed.</summary>
    UsbControlResult Eject(string deviceInstanceId);
}

/// <summary>
/// The Windows calls: IOCTL_DISK_SET_DISK_ATTRIBUTES on the physical drive of the volume, what
/// <c>diskpart attributes disk set readonly</c> does; CM_Request_Device_Eject on the device node,
/// what the "Safely remove" tray does. Both need the service's rights (LocalSystem).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUsbDeviceControl : IUsbDeviceControl
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    private const uint IoctlDiskGetDiskAttributes = 0x000700F0;
    private const uint IoctlDiskSetDiskAttributes = 0x0007C0F4;
    private const ulong DiskAttributeReadOnly = 0x2;
    private const int CrSuccess = 0;

    /// <inheritdoc />
    public UsbControlResult SetReadOnly(string driveLetter)
    {
        string letter = driveLetter.TrimEnd(':', '\\', '/').ToUpperInvariant();
        if (letter.Length != 1 || !char.IsLetter(letter[0]))
        {
            return UsbControlResult.Failed($"not a drive letter: {driveLetter}");
        }

        using SafeFileHandle volume = CreateFileW($@"\\.\{letter}:", GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (volume.IsInvalid)
        {
            return UsbControlResult.Failed($"open volume {letter}: {LastError()}");
        }

        var number = new StorageDeviceNumber();
        if (!DeviceIoControl(volume, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0, ref number, (uint)Marshal.SizeOf<StorageDeviceNumber>(), out _, IntPtr.Zero))
        {
            return UsbControlResult.Failed($"device number of {letter}: {LastError()}");
        }

        using SafeFileHandle disk = CreateFileW($@"\\.\PhysicalDrive{number.DeviceNumber}", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (disk.IsInvalid)
        {
            return UsbControlResult.Failed($"open PhysicalDrive{number.DeviceNumber}: {LastError()}");
        }

        var set = new SetDiskAttributes
        {
            Version = (uint)Marshal.SizeOf<SetDiskAttributes>(),
            Persist = 0, // until the device is unplugged: a key is not locked for every other machine
            Attributes = DiskAttributeReadOnly,
            AttributesMask = DiskAttributeReadOnly,
        };
        if (!DeviceIoControl(disk, IoctlDiskSetDiskAttributes, ref set, set.Version, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            return UsbControlResult.Failed($"set read-only on PhysicalDrive{number.DeviceNumber}: {LastError()}");
        }

        // Read back: the result claims a state the disk really has.
        var get = new GetDiskAttributes();
        if (!DeviceIoControl(disk, IoctlDiskGetDiskAttributes, IntPtr.Zero, 0, ref get, (uint)Marshal.SizeOf<GetDiskAttributes>(), out _, IntPtr.Zero))
        {
            return UsbControlResult.Failed($"read attributes of PhysicalDrive{number.DeviceNumber}: {LastError()}");
        }

        return (get.Attributes & DiskAttributeReadOnly) != 0
            ? UsbControlResult.Done
            : UsbControlResult.Failed($"PhysicalDrive{number.DeviceNumber} still writable after the call");
    }

    /// <inheritdoc />
    public UsbControlResult Eject(string deviceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
        {
            return UsbControlResult.Failed("empty device instance id");
        }

        int cr = CM_Locate_DevNodeW(out uint devInst, deviceInstanceId, 0);
        if (cr != CrSuccess)
        {
            return UsbControlResult.Failed($"CM_Locate_DevNode({deviceInstanceId}): CR 0x{cr:X}");
        }

        var veto = new StringBuilder(260);
        cr = CM_Request_Device_EjectW(devInst, out int vetoType, veto, (uint)veto.Capacity, 0);
        if (cr != CrSuccess)
        {
            return UsbControlResult.Failed($"CM_Request_Device_Eject: CR 0x{cr:X}, veto type {vetoType} {veto}".TrimEnd());
        }

        return UsbControlResult.Done;
    }

    private static string LastError()
    {
        int code = Marshal.GetLastWin32Error();
        return $"{new Win32Exception(code).Message} (0x{code:X})";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber
    {
        public int DeviceType;
        public int DeviceNumber;
        public int PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SetDiskAttributes
    {
        public uint Version;
        public byte Persist;
        public byte Reserved1A;
        public byte Reserved1B;
        public byte Reserved1C;
        public ulong Attributes;
        public ulong AttributesMask;
        public uint Reserved2A;
        public uint Reserved2B;
        public uint Reserved2C;
        public uint Reserved2D;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GetDiskAttributes
    {
        public uint Version;
        public uint Reserved1;
        public ulong Attributes;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize, ref StorageDeviceNumber outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref SetDiskAttributes inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize, ref GetDiskAttributes outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_EjectW(uint devInst, out int vetoType, StringBuilder vetoName, uint vetoNameLength, uint flags);
}
