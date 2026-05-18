namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Computes Shannon entropy of file content for ransomware detection.
/// Entropy value ranges from 0.0 (uniform) to 8.0 (maximum randomness/encryption).
/// Academic basis: Scaife et al. 2016 (CryptoDrop), Continella et al. 2016 (ShieldFS).
/// </summary>
public interface IEntropyCalculator
{
    /// <summary>
    /// Computes Shannon entropy of a file's content in bits per byte.
    /// Uses streaming I/O with sampling for files > 1 MB.
    /// Returns null if the file is unreadable or locked.
    /// </summary>
    /// <param name="filePath">Absolute path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entropy in bits/byte [0.0, 8.0], or null if unreadable.</returns>
    Task<double?> ComputeFileEntropyAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes Shannon entropy of an in-memory byte buffer.
    /// </summary>
    /// <param name="data">Byte data to analyze.</param>
    /// <returns>Entropy in bits/byte [0.0, 8.0].</returns>
    double ComputeEntropy(ReadOnlySpan<byte> data);
}
