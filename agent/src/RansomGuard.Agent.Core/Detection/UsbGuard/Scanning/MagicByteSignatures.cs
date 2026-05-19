namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Static database of magic byte signatures for 50+ file formats.
/// Used by <see cref="IMagicByteValidator"/> to detect extension mismatch attacks.
/// </summary>
public static class MagicByteSignatures
{
    /// <summary>
    /// Maps file extension to one or more magic byte sequences.
    /// Empty arrays indicate text-based formats with no fixed binary signature.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, byte[][]> Signatures =
        new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".png"] = [new byte[] { 0x89, 0x50, 0x4E, 0x47 }],
            [".jpg"] = [
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }
            ],
            [".jpeg"] = [
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }
            ],
            [".gif"] = [
                new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 },
                new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }
            ],
            [".bmp"] = [new byte[] { 0x42, 0x4D }],
            [".ico"] = [new byte[] { 0x00, 0x00, 0x01, 0x00 }],
            [".tiff"] = [new byte[] { 0x49, 0x49, 0x2A, 0x00 }, new byte[] { 0x4D, 0x4D, 0x00, 0x2A }],
            [".webp"] = [new byte[] { 0x52, 0x49, 0x46, 0x46 }],

            // Documents — PDF
            [".pdf"] = [new byte[] { 0x25, 0x50, 0x44, 0x46 }],

            // Documents — Microsoft legacy (OLE Compound)
            [".doc"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }],
            [".xls"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }],
            [".ppt"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }],
            [".msg"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }],

            // Documents — Office Open XML (ZIP-based)
            [".docx"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],
            [".xlsx"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],
            [".pptx"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],

            // Archives
            [".zip"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }, new byte[] { 0x50, 0x4B, 0x05, 0x06 }],
            [".rar"] = [new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }],
            [".7z"] = [new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }],
            [".gz"] = [new byte[] { 0x1F, 0x8B }],
            [".bz2"] = [new byte[] { 0x42, 0x5A, 0x68 }],
            [".xz"] = [new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }],
            [".cab"] = [new byte[] { 0x4D, 0x53, 0x43, 0x46 }],
            [".iso"] = [new byte[] { 0x43, 0x44, 0x30, 0x30, 0x31 }],

            // Executables
            [".exe"] = [new byte[] { 0x4D, 0x5A }],
            [".dll"] = [new byte[] { 0x4D, 0x5A }],
            [".sys"] = [new byte[] { 0x4D, 0x5A }],
            [".scr"] = [new byte[] { 0x4D, 0x5A }],
            [".com"] = [new byte[] { 0x4D, 0x5A }],
            [".msi"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }],
            [".elf"] = [new byte[] { 0x7F, 0x45, 0x4C, 0x46 }],
            [".class"] = [new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }],

            // Java / Android
            [".jar"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],
            [".apk"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],

            // Windows shortcuts
            [".lnk"] = [new byte[] { 0x4C, 0x00, 0x00, 0x00 }],

            // Media
            [".mp3"] = [new byte[] { 0x49, 0x44, 0x33 }, new byte[] { 0xFF, 0xFB }],
            [".mp4"] = [new byte[] { 0x00, 0x00, 0x00 }], // ftyp at offset 4-7
            [".avi"] = [new byte[] { 0x52, 0x49, 0x46, 0x46 }],
            [".wav"] = [new byte[] { 0x52, 0x49, 0x46, 0x46 }],
            [".flac"] = [new byte[] { 0x66, 0x4C, 0x61, 0x43 }],
            [".mkv"] = [new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }],

            // Database
            [".sqlite"] = [new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 }],

            // Script formats — no fixed binary signature
            [".bat"] = [],
            [".cmd"] = [],
            [".ps1"] = [],
            [".vbs"] = [],
            [".js"] = [],
            [".wsf"] = [],
            [".sh"] = [new byte[] { 0x23, 0x21 }], // shebang #!
            [".py"] = [],

            // Text
            [".txt"] = [],
            [".csv"] = [],
            [".xml"] = [],
            [".json"] = [],
            [".html"] = [],
            [".htm"] = [],
        };

    /// <summary>
    /// Extensions that represent PE executables (CWE-345 critical if disguised).
    /// </summary>
    public static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".scr", ".com", ".sys", ".msi"
    };

    /// <summary>PE magic bytes: "MZ" header.</summary>
    public static readonly byte[] PeMagicBytes = [0x4D, 0x5A];

    /// <summary>OLE Compound Document magic bytes.</summary>
    public static readonly byte[] OleMagicBytes = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
}
