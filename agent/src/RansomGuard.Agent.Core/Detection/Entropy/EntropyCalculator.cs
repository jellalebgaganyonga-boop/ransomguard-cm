using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Production Shannon entropy calculator with streaming I/O and large-file sampling.
/// Uses 256 frequency buckets (one per byte value) and computes H = -sum(p*log2(p)).
/// For files > 1 MB, samples first 64KB + middle 64KB + last 64KB = 192KB total.
/// </summary>
public sealed class EntropyCalculator : IEntropyCalculator
{
    private const int BufferSize = 4096;
    private const long SamplingThresholdBytes = 1024 * 1024; // 1 MB
    private const int SampleChunkSize = 64 * 1024; // 64 KB

    private readonly ILogger<EntropyCalculator> _logger;

    /// <summary>
    /// Initializes the entropy calculator.
    /// </summary>
    public EntropyCalculator(ILogger<EntropyCalculator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<double?> ComputeFileEntropyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true);

            long fileLength = fs.Length;
            if (fileLength == 0)
            {
                return 0.0;
            }

            if (fileLength <= SamplingThresholdBytes)
            {
                return await ComputeFullEntropyAsync(fs, fileLength, cancellationToken);
            }

            return await ComputeSampledEntropyAsync(fs, fileLength, cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Cannot read file for entropy: {FilePath}", filePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Access denied for entropy: {FilePath}", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public double ComputeEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return 0.0;
        }

        Span<long> freq = stackalloc long[256];
        freq.Clear();

        foreach (byte b in data)
        {
            freq[b]++;
        }

        return CalculateEntropyFromFrequencies(freq, data.Length);
    }

    private async Task<double> ComputeFullEntropyAsync(FileStream fs, long length, CancellationToken ct)
    {
        long[] freq = new long[256];
        byte[] buffer = new byte[BufferSize];
        long totalBytes = 0;

        int bytesRead;
        while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                freq[buffer[i]]++;
            }
            totalBytes += bytesRead;
        }

        return CalculateEntropyFromFrequencies(freq, totalBytes);
    }

    private async Task<double> ComputeSampledEntropyAsync(FileStream fs, long fileLength, CancellationToken ct)
    {
        long[] freq = new long[256];
        byte[] buffer = new byte[SampleChunkSize];
        long totalBytes = 0;

        // Sample 1: first 64 KB
        int read = await fs.ReadAsync(buffer.AsMemory(0, SampleChunkSize), ct);
        AccumulateFrequencies(freq, buffer.AsSpan(0, read));
        totalBytes += read;

        // Sample 2: middle 64 KB
        long middleOffset = (fileLength - SampleChunkSize) / 2;
        fs.Seek(middleOffset, SeekOrigin.Begin);
        read = await fs.ReadAsync(buffer.AsMemory(0, SampleChunkSize), ct);
        AccumulateFrequencies(freq, buffer.AsSpan(0, read));
        totalBytes += read;

        // Sample 3: last 64 KB
        fs.Seek(-SampleChunkSize, SeekOrigin.End);
        read = await fs.ReadAsync(buffer.AsMemory(0, SampleChunkSize), ct);
        AccumulateFrequencies(freq, buffer.AsSpan(0, read));
        totalBytes += read;

        return CalculateEntropyFromFrequencies(freq, totalBytes);
    }

    private static void AccumulateFrequencies(long[] freq, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            freq[b]++;
        }
    }

    private static double CalculateEntropyFromFrequencies(ReadOnlySpan<long> freq, long totalBytes)
    {
        if (totalBytes == 0) return 0.0;

        double entropy = 0.0;
        double invTotal = 1.0 / totalBytes;

        for (int i = 0; i < 256; i++)
        {
            if (freq[i] > 0)
            {
                double p = freq[i] * invTotal;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }
}
