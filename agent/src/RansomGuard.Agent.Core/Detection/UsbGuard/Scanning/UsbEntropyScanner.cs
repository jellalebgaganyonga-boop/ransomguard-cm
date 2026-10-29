using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.Entropy;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Looks for encrypted content on a USB device, and leaves alone the formats that are
/// compressed by design.
/// </summary>
/// <remarks>
/// <para>
/// Compressed bytes and encrypted bytes look the same to an entropy test: both sit at about
/// eight bits per byte. So a scanner that reports high entropy reports every PDF, every photo
/// and every Office document as well, unless it knows what it is looking at.
/// </para>
/// <para>
/// It did. The list of formats to forgive was written by extension and had no .pdf and no
/// .docx in it, so when the owner plugged in her own key on 2026-10-27 -- her documents,
/// nothing else -- it came back Critical, on PDFs reading 7.98. In the shipped Permissive mode
/// a Critical finding makes the drive read-only: a nurse would have found her key locked, with
/// "blocked by policy" as the explanation.
/// </para>
/// <para>
/// The formats are recognised by their magic bytes now. An extension is a claim, and files that
/// are not what they claim are the reason this scanner exists -- so a .pdf that does not begin
/// %PDF gets no forgiveness from its name, and a photo someone renamed gets no suspicion from
/// losing it. What is left -- high entropy and no header anyone recognises -- is reported Low:
/// worth saying, not on its own a reason to lock somebody's key.
/// </para>
/// </remarks>
public sealed class UsbEntropyScanner
{
    private const double HighEntropyThreshold = 7.9;
    private const long MinFileSizeForScan = 100 * 1024; // 100 KB

    /// <summary>
    /// What the first bytes of a file compressed by design look like. Read from the file, never
    /// from its name.
    /// </summary>
    /// <remarks>
    /// Office Open XML -- docx, xlsx, pptx -- is a zip, so it shares the PK header. mp4 and the
    /// other ISO-media files carry "ftyp" at offset 4, which is why that one is matched there
    /// rather than at the start.
    /// </remarks>
    private static readonly (int Offset, byte[] Magic)[] CompressedByDesign =
    [
        (0, [0x25, 0x50, 0x44, 0x46]),                          // %PDF
        (0, [0x50, 0x4B, 0x03, 0x04]),                          // zip, and so docx/xlsx/pptx
        (0, [0x50, 0x4B, 0x05, 0x06]),                          // empty zip
        (0, [0x50, 0x4B, 0x07, 0x08]),                          // spanned zip
        (0, [0xFF, 0xD8, 0xFF]),                                // JPEG
        (0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),  // PNG
        (0, [0x47, 0x49, 0x46, 0x38]),                          // GIF8
        (0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),              // 7z
        (0, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]),              // RAR
        (0, [0x1F, 0x8B]),                                      // gzip
        (0, [0x42, 0x5A, 0x68]),                                // bzip2
        (0, [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]),              // xz
        (0, [0x49, 0x44, 0x33]),                                // MP3 with an ID3 tag
        (0, [0x52, 0x49, 0x46, 0x46]),                          // RIFF: avi, wav, webp
        (0, [0x1A, 0x45, 0xDF, 0xA3]),                          // Matroska: mkv, webm
        (0, [0x66, 0x4C, 0x61, 0x43]),                          // FLAC
        (4, [0x66, 0x74, 0x79, 0x70]),                          // ftyp: mp4, m4a, mov
    ];

    /// <summary>Enough to hold the longest signature above, at its offset.</summary>
    private const int HeaderBytes = 12;

    private readonly IEntropyCalculator _calculator;
    private readonly ILogger<UsbEntropyScanner> _logger;

    /// <summary>Initializes the USB entropy scanner.</summary>
    public UsbEntropyScanner(IEntropyCalculator calculator, ILogger<UsbEntropyScanner> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a file has suspiciously high entropy (possible encryption).
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Finding if high entropy detected, null otherwise.</returns>
    public async Task<EntropyFinding?> ScanAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < MinFileSizeForScan) return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (await IsCompressedByDesignAsync(filePath, ct)) return null;

            double? entropy = await _calculator.ComputeFileEntropyAsync(filePath, ct);
            if (entropy is null || entropy.Value <= HighEntropyThreshold) return null;

            // Said at Information, not Warning: on a key full of somebody's documents this used
            // to fill the log with warnings about files that were exactly what they looked like.
            _logger.LogInformation(
                "USB GUARD: high entropy {Entropy:F2} on {File}, and no header this agent recognises",
                entropy.Value, filePath);

            return new EntropyFinding
            {
                FilePath = filePath,
                Entropy = entropy.Value,
                FileExtension = ext,
                // Low on its own. Entropy cannot tell encrypted from compressed, so it is a
                // reason to look, never a reason to act on somebody's key by itself: that takes
                // a real sign, which is what the bootable, autorun and executable detectors find.
                Severity = ScanSeverity.Low
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Entropy scan failed for {File}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Does this file begin like a format that is compressed by design?
    /// </summary>
    /// <remarks>
    /// By its bytes. A file that claims .pdf and does not begin %PDF is looked at like any
    /// other; a photo somebody renamed to .dat is still a photo.
    /// </remarks>
    private static async Task<bool> IsCompressedByDesignAsync(string filePath, CancellationToken ct)
    {
        byte[] header = new byte[HeaderBytes];
        int read;
        await using (FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            read = await stream.ReadAsync(header.AsMemory(0, HeaderBytes), ct).ConfigureAwait(false);
        }

        foreach ((int offset, byte[] magic) in CompressedByDesign)
        {
            if (read < offset + magic.Length)
            {
                continue;
            }

            if (header.AsSpan(offset, magic.Length).SequenceEqual(magic))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Finding from entropy scan.
/// </summary>
public sealed record EntropyFinding
{
    /// <summary>Path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Measured entropy value (bits/byte).</summary>
    public required double Entropy { get; init; }

    /// <summary>File extension.</summary>
    public required string FileExtension { get; init; }

    /// <summary>Severity (Medium for encrypted content on USB).</summary>
    public required ScanSeverity Severity { get; init; }
}
