using RansomGuard.Agent.Core.Detection.UsbGuard;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// A boot signature is not a bootable device.
///
/// <para>
/// USB GUARD used to decide "bootable" from the two bytes 0x55AA at offset 510 of the first
/// sector. <c>\.\E:</c> opens the volume, and the Volume Boot Record of every FAT32 and NTFS
/// volume ends in those bytes -- formatting a blank key puts them there. On 2026-10-26 that was
/// pointed at a real Kingston key with the owner's documents on it and called it Critical. In the
/// mode the product ships in, Critical makes the drive read-only: a nurse's key locked, reason
/// "blocked by policy".
/// </para>
///
/// <para>
/// These tests exist so that cannot come back. The signature is still read and still reported --
/// it is worth knowing -- but a device is bootable when a bootloader is on it, and not otherwise.
/// </para>
/// </summary>
public sealed class BootableUsbDetectorTests : IDisposable
{
    private readonly string _volume =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rg_usb_{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_volume, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void An_ordinary_formatted_key_carries_a_boot_signature()
    {
        // Not a defect in the signature check: this is simply true of every formatted volume,
        // which is exactly why nothing may be decided on it.
        var vbr = new byte[520];
        vbr[510] = 0x55;
        vbr[511] = 0xAA;

        BootableUsbDetector.HasBootSignature(vbr).ShouldBeTrue();
    }

    [Fact]
    public void A_GPT_header_is_a_signature_too()
    {
        var sectors = new byte[520];
        "EFI PART"u8.CopyTo(sectors.AsSpan(512));

        BootableUsbDetector.HasBootSignature(sectors).ShouldBeTrue();
    }

    [Fact]
    public void An_unformatted_volume_has_no_signature()
    {
        BootableUsbDetector.HasBootSignature(new byte[520]).ShouldBeFalse();
    }

    [Fact]
    public void A_key_with_documents_on_it_is_not_bootable()
    {
        Directory.CreateDirectory(Path.Combine(_volume, "Photos"));
        File.WriteAllText(Path.Combine(_volume, "budget.xlsx"), "not really a spreadsheet");
        File.WriteAllText(Path.Combine(_volume, "Photos", "holiday.jpg"), "not really a photo");
        File.WriteAllText(Path.Combine(_volume, "setup.exe"), "an executable, which is not a bootloader");

        // The one that matters. An executable on a key is not a reason to lock somebody out of it.
        BootableUsbDetector.FindBootloaderIn(_volume).ShouldBeNull();
    }

    [Theory]
    [InlineData(@"EFI\BOOT\BOOTX64.EFI")]   // how UEFI boots removable media
    [InlineData("bootmgr")]                  // Windows installer sticks
    [InlineData(@"boot\bcd")]
    [InlineData("ldlinux.sys")]              // syslinux, as live Linux images use
    [InlineData(@"boot\grub\grub.cfg")]
    [InlineData(@"casper\vmlinuz")]          // an Ubuntu live image
    public void A_real_bootloader_makes_it_bootable(string relative)
    {
        string full = Path.Combine(_volume, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "bootloader");

        BootableUsbDetector.FindBootloaderIn(_volume).ShouldBe(relative);
    }

    [Fact]
    public void The_bootloader_is_found_whatever_the_case_of_the_name()
    {
        // Windows file names are not case sensitive, and a key written on Linux may spell it
        // either way. Missing a Windows installer because it wrote "BOOTMGR" would be silly.
        File.WriteAllText(Path.Combine(_volume, "BOOTMGR"), "bootloader");

        BootableUsbDetector.FindBootloaderIn(_volume).ShouldNotBeNull();
    }

    [Fact]
    public void Every_path_it_looks_for_is_one_it_recognises()
    {
        BootableUsbDetector.KnownBootloaders.ShouldNotBeEmpty();
        foreach (string path in BootableUsbDetector.KnownBootloaders)
        {
            BootableUsbDetector.NamesABootloader(path).ShouldBeTrue(path);
        }

        BootableUsbDetector.NamesABootloader("budget.xlsx").ShouldBeFalse();
        BootableUsbDetector.NamesABootloader("setup.exe").ShouldBeFalse();
    }
}
