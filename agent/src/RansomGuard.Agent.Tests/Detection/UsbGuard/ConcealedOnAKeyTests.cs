using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// What was actually on a key, and what the scan did about it.
///
/// <para>
/// On 2026-10-29 the owner's key was scanned on the bench and the module reported "2 files
/// scanned, 2 skipped, 0 findings, severity: None". The key held 773 files. 769 of them were in
/// a folder called RECYCLER.BIN -- not the real `$RECYCLE.BIN` -- marked ReadOnly, Hidden and
/// System, with a desktop.ini claiming the recycle bin's CLSID so that Explorer drew it as an
/// empty bin. Inside: 766 of the owner's own documents under base64 names
/// (JFJTUzJCVlNfTWF0aHMgLSA2ZW1lLmRvY3g= is "$RSS2BVS_Maths - 6eme.docx"), and tmp.bat, which
/// appends systeminfo, ipconfig /all, netstat -ano, arp -a and tasklist /v to a file on the key
/// and then deletes itself.
/// </para>
///
/// <para>
/// The module walked past all of it because its enumeration carried
/// `AttributesToSkip = FileAttributes.System`. That is a sensible default on a system drive and
/// the wrong one on a USB key: Hidden + System is not where Windows keeps its furniture on
/// removable media, it is where USB malware hides. Windows' own two folders are skipped by name
/// now, and nothing is skipped for an attribute it can set on itself.
/// </para>
/// </summary>
public sealed class ConcealedOnAKeyTests : IDisposable
{
    private readonly string _drive = Directory.CreateTempSubdirectory("rg-usb-concealed").FullName;

    public void Dispose() => Directory.Delete(_drive, recursive: true);

    private static UsbContentScanner Scanner() => new(
        new MagicByteValidator(NullLogger<MagicByteValidator>.Instance),
        new AutorunInfDetector(NullLogger<AutorunInfDetector>.Instance),
        new SuspiciousLnkDetector(NullLogger<SuspiciousLnkDetector>.Instance),
        new ArchiveScanner(NullLogger<ArchiveScanner>.Instance),
        new UsbEntropyScanner(new EntropyCalculator(NullLogger<EntropyCalculator>.Instance),
            NullLogger<UsbEntropyScanner>.Instance),
        NullLogger<UsbContentScanner>.Instance);

    private UsbDevice Device() => new()
    {
        Id = Guid.NewGuid(),
        DeviceInstanceId = @"USB\VID_0930&PID_6544\KEY",
        SerialNumber = "KEY",
        VendorId = "0930",
        ProductId = "6544",
        Manufacturer = "TOSHIBA",
        ProductDescription = "TransMemory",
        Model = "TransMemory",
        DriveLetter = _drive,
        CapacityBytes = 14L * 1024 * 1024 * 1024,
        DeviceClass = UsbDeviceClass.MassStorage,
        BusType = UsbBusType.Usb20,
        IsRemovable = true,
        IsBootable = false,
        ConnectedAt = DateTime.UtcNow,
    };

    private static UsbPolicy Policy() => new()
    {
        Id = Guid.NewGuid(),
        Mode = UsbOperatingMode.Permissive,
        MaxFileSizeForScanMB = 100,
        MaxScanDurationSeconds = 30,
        ScanArchiveContents = true,
        BlockBootableUsb = true,
        AlertOnHidDevice = false,
        AlertOnNetworkDevice = false,
        SuspiciousExtensionsJson = "[]",
        UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>A folder that hides what is in it, as the real key's RECYCLER.BIN did.</summary>
    private string HiddenSystemFolder(string name)
    {
        string path = Path.Combine(_drive, name);
        Directory.CreateDirectory(path);
        File.SetAttributes(path, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        return path;
    }

    private static void WriteRecycleBinDisguise(string folder) =>
        File.WriteAllText(Path.Combine(folder, "desktop.ini"),
            "[.ShellClassInfo]\r\nCLSID={645FF040-5081-101B-9F08-00AA002F954E}\r\n");

    [Fact]
    public async Task A_script_hidden_in_a_system_folder_is_read_and_reported()
    {
        string hidden = HiddenSystemFolder("RECYCLER.BIN");
        // The real one, shortened: it collects the machine's details onto the key.
        File.WriteAllText(Path.Combine(hidden, "tmp.bat"),
            "%comspec% /q /c systeminfo >sys.info\r\n%comspec% /q /c ipconfig /all >>sys.info\r\ndel %0\r\n");

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        report.FilesScanned.ShouldBeGreaterThan(0, "the folder's attributes are not a reason to look away");
        report.Findings.ShouldContain(f => f.FindingType == "ConcealedExecutable");
        ((int)report.OverallSeverity).ShouldBeGreaterThanOrEqualTo((int)ScanSeverity.High);
    }

    [Fact]
    public async Task A_folder_wearing_the_recycle_bins_face_is_a_finding()
    {
        string hidden = HiddenSystemFolder("RECYCLER.BIN");
        WriteRecycleBinDisguise(hidden);
        // The documents it swallowed, under the names the worm gave them.
        File.WriteAllText(Path.Combine(hidden, "JFJTUzJCVlNfTWF0aHMgLSA2ZW1lLmRvY3g="), "a document");

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        UsbFileFinding finding = report.Findings.ShouldHaveSingleItem(
            "the disguise is one finding, not one per file it hides");
        finding.FindingType.ShouldBe("DisguisedRecycleBin");
        finding.Severity.ShouldBe(ScanSeverity.High);
        finding.Description.ShouldContain("RECYCLER.BIN");
    }

    [Fact]
    public async Task The_real_recycle_bin_is_left_alone()
    {
        // Windows puts this on a removable NTFS volume itself, with the same desktop.ini. It is
        // skipped by name, which is a closed list -- not by its attributes, which anything can set.
        string real = HiddenSystemFolder("$RECYCLE.BIN");
        WriteRecycleBinDisguise(real);
        File.WriteAllText(Path.Combine(real, "deleted.docx"), "something the user deleted");

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task System_volume_information_is_still_skipped()
    {
        string svi = HiddenSystemFolder("System Volume Information");
        File.WriteAllText(Path.Combine(svi, "tracking.log"), "the volume's own bookkeeping");
        File.WriteAllText(Path.Combine(svi, "WPSettings.dat"), "more of it");

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        report.Findings.ShouldBeEmpty();
        report.FilesScanned.ShouldBe(0, "Windows' own folder is not this module's business");
    }

    [Fact]
    public async Task An_ordinary_key_is_still_left_alone()
    {
        // The other half of the bargain: reading hidden folders must not turn an ordinary key
        // into a finding. A Word file is a zip, and a zip reads near 8 bits per byte.
        File.WriteAllBytes(Path.Combine(_drive, "CHAMBRE A LOUER.docx"),
            [0x50, 0x4B, 0x03, 0x04, .. new byte[8192]]);
        File.WriteAllText(Path.Combine(_drive, "notes.txt"), "an ordinary note");

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        ((int)report.OverallSeverity).ShouldBeLessThan((int)ScanSeverity.Medium);
    }
}
