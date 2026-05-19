using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>
/// Detects bootable USB devices by reading MBR/GPT signatures from the first sectors.
/// MBR: bytes 0x55 0xAA at offset 510. GPT: "EFI PART" at LBA 1 (offset 512).
/// CWE-862 mitigation: bootable USB devices are flagged for policy enforcement.
/// </summary>
public sealed class BootableUsbDetector
{
    private readonly ILogger<BootableUsbDetector> _logger;

    /// <summary>Initializes the bootable USB detector.</summary>
    public BootableUsbDetector(ILogger<BootableUsbDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if a drive contains MBR or GPT boot signatures.
    /// </summary>
    /// <param name="driveLetter">Drive letter (e.g., "E:").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if bootable signature found.</returns>
    public async Task<bool> IsBootableAsync(string driveLetter, CancellationToken ct = default)
    {
        try
        {
            string physicalPath = $@"\\.\{driveLetter}";
            await using var stream = new FileStream(
                physicalPath,
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);

            var buffer = new byte[520]; // 512 (MBR) + 8 (GPT header start)
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 520), ct);

            if (bytesRead < 512) return false;

            // MBR signature: 0x55 0xAA at offset 510
            if (buffer[510] == 0x55 && buffer[511] == 0xAA)
            {
                _logger.LogInformation("USB GUARD: MBR boot signature detected on {Drive}", driveLetter);
                return true;
            }

            // GPT signature: "EFI PART" at offset 512 (LBA 1)
            if (bytesRead >= 520 && buffer.AsSpan(512, 8).SequenceEqual("EFI PART"u8))
            {
                _logger.LogInformation("USB GUARD: GPT boot signature detected on {Drive}", driveLetter);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot read boot sector from {Drive}", driveLetter);
            return false;
        }
    }

    /// <summary>
    /// Checks a raw byte buffer for MBR/GPT signatures (for unit testing without physical disk).
    /// </summary>
    public static bool HasBootSignature(ReadOnlySpan<byte> firstSectors)
    {
        if (firstSectors.Length < 512) return false;

        // MBR
        if (firstSectors[510] == 0x55 && firstSectors[511] == 0xAA)
            return true;

        // GPT
        if (firstSectors.Length >= 520 && firstSectors.Slice(512, 8).SequenceEqual("EFI PART"u8))
            return true;

        return false;
    }
}
