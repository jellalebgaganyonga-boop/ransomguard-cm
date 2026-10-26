using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>What was found on a removable volume when looking for a way to boot from it.</summary>
/// <param name="IsBootable">A bootloader is actually present. This is the one worth acting on.</param>
/// <param name="Evidence">The file that says so, for the alert and the log. Null when not bootable.</param>
/// <param name="HasBootSignature">
/// The first sector ends 0x55AA, or LBA 1 reads "EFI PART". On its own this means almost nothing:
/// see <see cref="BootableUsbDetector"/>.
/// </param>
public readonly record struct BootableVerdict(bool IsBootable, string? Evidence, bool HasBootSignature);

/// <summary>
/// Whether a removable volume can actually be booted from.
///
/// <para>
/// This used to answer that question with the boot signature alone: 0x55AA at offset 510 of the
/// first sector. That was wrong, and wrong in the direction that hurts. <c>\.\E:</c> opens the
/// *volume*, whose first sector is the Volume Boot Record, and the VBR of every FAT32 and NTFS
/// volume ends in those two bytes. Formatting a blank key puts them there. So the check answered
/// "is this formatted", and the answer was yes for every key anybody owns.
/// </para>
///
/// <para>
/// On 2026-10-26 that was pointed at a real key for the first time -- an ordinary Kingston with
/// the owner's documents on it -- and USB GUARD called it Critical for its boot signature. In the
/// mode the product ships in, Critical means the drive is made read-only. A nurse would have
/// found her key locked and been told "blocked by policy".
/// </para>
///
/// <para>
/// So the signature is now recorded and ignored, and the question is answered by looking for a
/// bootloader: the files that have to be there for firmware or a BIOS to hand control to the key.
/// A data key does not have them. A Windows installer, a Linux live image or a rescue stick does,
/// and that is a device worth a word about on a hospital workstation.
/// </para>
/// </summary>
public sealed class BootableUsbDetector
{
    /// <summary>
    /// The files that make a volume bootable. Each is a real bootloader hand-off point rather
    /// than a heuristic: the UEFI removable-media path, the Windows boot managers, and the
    /// loaders that live Linux images are built with.
    /// </summary>
    private static readonly string[] BootloaderPaths =
    [
        @"EFI\BOOT\BOOTX64.EFI",
        @"EFI\BOOT\BOOTIA32.EFI",
        @"EFI\BOOT\BOOTAA64.EFI",
        @"EFI\BOOT\BOOTARM.EFI",
        "bootmgr",
        "bootmgr.efi",
        "ntldr",
        @"boot\bcd",
        @"isolinux\isolinux.bin",
        @"isolinux\isolinux.cfg",
        "isolinux.cfg",
        @"syslinux\syslinux.cfg",
        "syslinux.cfg",
        "ldlinux.sys",
        @"boot\grub\grub.cfg",
        @"grub\grub.cfg",
        @"casper\vmlinuz",
        @"live\vmlinuz",
    ];

    private readonly ILogger<BootableUsbDetector> _logger;

    /// <summary>Initializes the bootable USB detector.</summary>
    public BootableUsbDetector(ILogger<BootableUsbDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Looks at a removable volume and says whether it can be booted from, and why.
    /// </summary>
    /// <param name="driveLetter">Drive letter, for example "E:".</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<BootableVerdict> InspectAsync(string driveLetter, CancellationToken ct = default)
    {
        bool signature = await HasBootSignatureAsync(driveLetter, ct).ConfigureAwait(false);

        string? evidence = FindBootloader(driveLetter);
        if (evidence is not null)
        {
            _logger.LogWarning("USB GUARD: bootable volume on {Drive}: {Evidence}", driveLetter, evidence);
            return new BootableVerdict(true, evidence, signature);
        }

        if (signature)
        {
            // Said at Information and nothing else. Every formatted key lands here.
            _logger.LogInformation(
                "USB GUARD: {Drive} has a boot signature but no bootloader; an ordinary formatted volume",
                driveLetter);
        }

        return new BootableVerdict(false, null, signature);
    }

    /// <summary>The first bootloader found on the volume, or null.</summary>
    private string? FindBootloader(string driveLetter) =>
        FindBootloaderIn(driveLetter.EndsWith(':') ? driveLetter + @"\" : driveLetter);

    /// <summary>
    /// The first bootloader found under <paramref name="volumeRoot"/>, or null.
    ///
    /// <para>
    /// Takes a path rather than a drive letter so it can be pointed at a folder in a test. The
    /// thing worth testing is which files count as a bootloader, and that needs no disk.
    /// </para>
    /// </summary>
    public static string? FindBootloaderIn(string volumeRoot)
    {
        foreach (string relative in BootloaderPaths)
        {
            try
            {
                if (File.Exists(Path.Combine(volumeRoot, relative)))
                {
                    return relative;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // An unreadable path is not evidence either way; keep looking.
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the volume carries a boot signature. Kept because it is worth recording, never
    /// because it decides anything: see the note on this class.
    /// </summary>
    private async Task<bool> HasBootSignatureAsync(string driveLetter, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                $@"\.\{driveLetter}",
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);

            var buffer = new byte[520]; // 512 (VBR/MBR) + 8 ("EFI PART" at LBA 1)
            int read = await stream.ReadAsync(buffer.AsMemory(0, 520), ct).ConfigureAwait(false);
            return HasBootSignature(buffer.AsSpan(0, Math.Max(read, 0)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot read boot sector from {Drive}", driveLetter);
            return false;
        }
    }

    /// <summary>
    /// Whether these first sectors carry an MBR or GPT signature.
    ///
    /// <para>
    /// True for essentially every formatted volume, which is why nothing is decided on it. It is
    /// reported alongside the verdict so that a bootable device's alert can say both things.
    /// </para>
    /// </summary>
    public static bool HasBootSignature(ReadOnlySpan<byte> firstSectors)
    {
        if (firstSectors.Length < 512) return false;

        if (firstSectors[510] == 0x55 && firstSectors[511] == 0xAA)
            return true;

        if (firstSectors.Length >= 520 && firstSectors.Slice(512, 8).SequenceEqual("EFI PART"u8))
            return true;

        return false;
    }

    /// <summary>
    /// Whether any of these file names, relative to a volume root, is a bootloader. Exposed so the
    /// list can be tested without a disk.
    /// </summary>
    public static bool NamesABootloader(string relativePath) =>
        BootloaderPaths.Any(p => string.Equals(p, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bootloader paths this looks for, for tests and for documentation.</summary>
    public static IReadOnlyList<string> KnownBootloaders => BootloaderPaths;
}
