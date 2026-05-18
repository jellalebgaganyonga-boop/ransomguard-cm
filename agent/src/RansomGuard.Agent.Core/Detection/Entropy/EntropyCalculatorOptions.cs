namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Configuration for the Shannon entropy calculator.
/// Controls buffer sizes and sampling thresholds.
/// </summary>
public sealed record EntropyCalculatorOptions
{
    /// <summary>Streaming I/O buffer size in bytes.</summary>
    public int BufferSizeBytes { get; init; } = 4096;

    /// <summary>File size threshold above which sampling is used instead of full read.</summary>
    public long SamplingThresholdBytes { get; init; } = 1_048_576; // 1 MB

    /// <summary>Size of each sample section in bytes (first + middle + last).</summary>
    public int SampleSizePerSection { get; init; } = 65_536; // 64 KB
}
