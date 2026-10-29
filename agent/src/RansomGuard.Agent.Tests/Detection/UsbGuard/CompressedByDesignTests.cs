using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// An ordinary key is left alone.
///
/// <para>
/// The owner plugged in her own Kingston key -- her documents, nothing else -- and USB GUARD
/// called it Critical. The content scan measures entropy and treats anything above 7.9 as
/// possibly encrypted, and the formats it forgave were listed by extension: .zip, .jpg, .mp4 and
/// so on, with no .pdf and no .docx. A PDF is compressed by design and reads 7.98, so a folder of
/// professional summaries looked exactly like a folder of ransomware output.
/// </para>
///
/// <para>
/// In the shipped Permissive mode a Critical finding makes the drive read-only. A nurse plugging
/// in her key and finding it locked, with "blocked by policy" as the explanation, is not a
/// ransomware defence; it is the thing that teaches people to work around the agent.
/// </para>
///
/// <para>
/// So the formats that are compressed by design are recognised by their magic bytes, not their
/// name -- an extension is a claim, and the whole point of this scanner is files that are not
/// what they claim.
/// </para>
/// </summary>
public sealed class CompressedByDesignTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rg-usb-entropy").FullName;
    private readonly UsbEntropyScanner _scanner =
        new(new EntropyCalculator(NullLogger<EntropyCalculator>.Instance),
            NullLogger<UsbEntropyScanner>.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>High-entropy bytes, deterministic, behind whatever header is given.</summary>
    private string File(string name, params byte[] header)
    {
        string path = Path.Combine(_dir, name);
        byte[] body = new byte[256 * 1024];
        new Random(20261028).NextBytes(body);          // 8.0 bits/byte, like any compressed stream
        byte[] all = [.. header, .. body];
        System.IO.File.WriteAllBytes(path, all);
        return path;
    }

    public static TheoryData<string, byte[]> CompressedByDesign() => new()
    {
        { "Professional Summary.pdf", [0x25, 0x50, 0x44, 0x46] },             // %PDF
        { "patients.docx", [0x50, 0x4B, 0x03, 0x04] },                        // OOXML is a zip
        { "rota.xlsx", [0x50, 0x4B, 0x03, 0x04] },
        { "training.pptx", [0x50, 0x4B, 0x03, 0x04] },
        { "backup.zip", [0x50, 0x4B, 0x03, 0x04] },
        { "ward.jpg", [0xFF, 0xD8, 0xFF, 0xE0] },
        { "scan.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { "handover.mp4", [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70] }, // ....ftyp
        { "archive.7z", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C] },
        { "archive.rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07] },
        { "logs.gz", [0x1F, 0x8B] },
    };

    [Theory]
    [MemberData(nameof(CompressedByDesign))]
    public async Task A_format_that_is_compressed_by_design_is_not_a_finding(string name, byte[] header)
    {
        EntropyFinding? finding = await _scanner.ScanAsync(File(name, header));

        finding.ShouldBeNull($"{name} is compressed by design: high entropy is what it is made of");
    }

    [Fact]
    public async Task A_PDF_whose_name_was_changed_is_still_a_PDF()
    {
        // The magic bytes are the point. Renaming a holiday photo does not make it suspicious.
        EntropyFinding? finding = await _scanner.ScanAsync(File("summary.dat", 0x25, 0x50, 0x44, 0x46));

        finding.ShouldBeNull();
    }

    [Fact]
    public async Task Something_that_only_claims_to_be_a_PDF_is_still_looked_at()
    {
        // The other half: an extension is a claim. A file called .pdf that is not one gets no
        // forgiveness from being called .pdf -- which is the behaviour this scanner exists for.
        EntropyFinding? finding = await _scanner.ScanAsync(File("invoice.pdf", 0xDE, 0xAD, 0xBE, 0xEF));

        finding.ShouldNotBeNull();
        finding.Entropy.ShouldBeGreaterThan(7.9);
    }

    [Fact]
    public async Task An_unrecognised_high_entropy_file_is_reported_but_not_as_a_crisis()
    {
        // What encrypted output actually looks like: no known header at all. It is worth saying,
        // and it is not on its own a reason to lock somebody's key -- that needs a real sign,
        // which is what the bootable, autorun and executable detectors are for.
        EntropyFinding? finding = await _scanner.ScanAsync(File("patients.locked"));

        finding.ShouldNotBeNull();
        finding.Severity.ShouldBe(ScanSeverity.Low);
    }
}

/// <summary>
/// The whole scan of a key, not one file of it: what the agent decides about the device.
///
/// <para>
/// This is the line the owner's key crossed. An alert is raised, and an action taken in the
/// shipped Permissive mode, when the worst finding on the device reaches Medium -- so what
/// matters is not whether a PDF is noticed but whether a key of ordinary documents reaches that
/// threshold at all. It did, and the drive would have been made read-only.
/// </para>
/// </summary>
public sealed class OrdinaryKeyIsLeftAloneTests : IDisposable
{
    private readonly string _drive = Directory.CreateTempSubdirectory("rg-usb-key").FullName;

    public void Dispose() => Directory.Delete(_drive, recursive: true);

    private string Put(string name, byte[] header, int kilobytes = 256)
    {
        string path = Path.Combine(_drive, name);
        byte[] body = new byte[kilobytes * 1024];
        new Random(20261028).NextBytes(body);
        System.IO.File.WriteAllBytes(path, [.. header, .. body]);
        return path;
    }

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
        DeviceInstanceId = @"USB\VID_13FE&PID_4500\OWNERKEY",
        SerialNumber = "OWNERKEY",
        VendorId = "13FE",
        ProductId = "4500",
        Manufacturer = "Kingston Technology Company Inc.",
        ProductDescription = "USB DISK 2.0",
        Model = "USB DISK",
        DriveLetter = _drive,
        CapacityBytes = 7L * 1024 * 1024 * 1024,
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

    [Fact]
    public async Task A_key_of_documents_photos_and_office_files_raises_nothing_to_act_on()
    {
        // The owner's key, in miniature: professional summaries, a photo, a spreadsheet.
        Put("Professional Summary Mahamat Nour.pdf", [0x25, 0x50, 0x44, 0x46]);
        Put("Professional Summary Syldie Bizimana.pdf", [0x25, 0x50, 0x44, 0x46]);
        Put("rota.xlsx", [0x50, 0x4B, 0x03, 0x04]);
        Put("ward.jpg", [0xFF, 0xD8, 0xFF, 0xE0]);

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        // Below Medium is the whole point: Medium is where an alert is raised and, in the
        // shipped mode, where the drive is made read-only.
        ((int)report.OverallSeverity).ShouldBeLessThan((int)ScanSeverity.Medium);
    }

    [Fact]
    public async Task A_key_that_runs_something_when_it_is_plugged_in_is_still_caught()
    {
        // The line that must not move. autorun.inf pointing at an executable is a real sign,
        // and the fix for the false positive must not have cost this.
        System.IO.File.WriteAllText(Path.Combine(_drive, "autorun.inf"),
            "[autorun]\r\nopen=payload.exe\r\nshell\\open\\command=payload.exe\r\n");
        Put("payload.exe", [0x4D, 0x5A], kilobytes: 64);

        UsbScanReport report = await Scanner().ScanAsync(Device(), Policy());

        ((int)report.OverallSeverity).ShouldBeGreaterThanOrEqualTo((int)ScanSeverity.High);
        report.Findings.ShouldContain(f => f.FindingType == "Autorun");
    }
}
