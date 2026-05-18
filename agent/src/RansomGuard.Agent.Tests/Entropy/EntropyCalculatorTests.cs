using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Entropy;
using Shouldly;

namespace RansomGuard.Agent.Tests.Entropy;

/// <summary>
/// Tests for <see cref="EntropyCalculator"/> Shannon entropy computation.
/// Validates against known entropy ranges for text, encrypted, and compressed data.
/// </summary>
public sealed class EntropyCalculatorTests : IDisposable
{
    private readonly EntropyCalculator _calculator;
    private readonly string _testDir;

    public EntropyCalculatorTests()
    {
        _calculator = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_entropy_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void ComputeEntropy_AllZeros_ReturnsZero()
    {
        byte[] data = new byte[1024];
        double entropy = _calculator.ComputeEntropy(data);
        entropy.ShouldBe(0.0);
    }

    [Fact]
    public void ComputeEntropy_EmptyData_ReturnsZero()
    {
        double entropy = _calculator.ComputeEntropy(ReadOnlySpan<byte>.Empty);
        entropy.ShouldBe(0.0);
    }

    [Fact]
    public void ComputeEntropy_PlainFrenchText_ReturnsLowEntropy()
    {
        // Realistic French medical text — structured, repetitive characters
        string medicalText = """
            DOSSIER MEDICAL CONFIDENTIEL
            Patient : Mballa Jean-Pierre
            Date de naissance : 15/03/1978
            Medecin traitant : Dr. Nkeng Patrice
            Antecedents : Paludisme severe, Hypertension arterielle
            Traitement : Amlodipine 10mg, Metformine 850mg
            Consultation du 01/05/2026 : Examen clinique sans particularite.
            Tension arterielle 130/85 mmHg. Temperature 37.2C.
            """;
        // Repeat to get a realistic file size
        StringBuilder sb = new();
        for (int i = 0; i < 10; i++) sb.Append(medicalText);

        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        double entropy = _calculator.ComputeEntropy(data);

        // French text entropy: typically 4.0-5.5 bits/byte
        entropy.ShouldBeGreaterThan(3.5);
        entropy.ShouldBeLessThan(5.5);
    }

    [Fact]
    public void ComputeEntropy_EncryptedData_ReturnsHighEntropy()
    {
        // AES-encrypted data should be near-maximum entropy
        byte[] data = RandomNumberGenerator.GetBytes(4096);
        double entropy = _calculator.ComputeEntropy(data);

        // Cryptographic random: 7.9+ bits/byte
        entropy.ShouldBeGreaterThan(7.8);
        entropy.ShouldBeLessThanOrEqualTo(8.0);
    }

    [Fact]
    public void ComputeEntropy_SingleByteValue_ReturnsZero()
    {
        byte[] data = Enumerable.Repeat((byte)0x42, 1000).ToArray();
        double entropy = _calculator.ComputeEntropy(data);
        entropy.ShouldBe(0.0);
    }

    [Fact]
    public void ComputeEntropy_TwoEqualValues_ReturnsOne()
    {
        // 50% 'A', 50% 'B' → entropy = 1.0 bit/byte
        byte[] data = new byte[1000];
        for (int i = 0; i < 500; i++) data[i] = (byte)'A';
        for (int i = 500; i < 1000; i++) data[i] = (byte)'B';

        double entropy = _calculator.ComputeEntropy(data);
        entropy.ShouldBe(1.0, 0.01);
    }

    [Fact]
    public async Task ComputeFileEntropy_PlaintextFile_ReturnsLowEntropy()
    {
        string filePath = Path.Combine(_testDir, "medical-record.txt");
        StringBuilder sb = new();
        for (int i = 0; i < 50; i++)
            sb.AppendLine("Patient Mballa Jean-Pierre, consultation du 01/05/2026, tension 130/85, traitement Amlodipine.");
        await File.WriteAllTextAsync(filePath, sb.ToString());

        double? entropy = await _calculator.ComputeFileEntropyAsync(filePath);

        entropy.ShouldNotBeNull();
        entropy!.Value.ShouldBeGreaterThan(3.5);
        entropy.Value.ShouldBeLessThan(5.5);
    }

    [Fact]
    public async Task ComputeFileEntropy_EncryptedFile_ReturnsHighEntropy()
    {
        string filePath = Path.Combine(_testDir, "encrypted.bin");
        byte[] encrypted = RandomNumberGenerator.GetBytes(8192);
        await File.WriteAllBytesAsync(filePath, encrypted);

        double? entropy = await _calculator.ComputeFileEntropyAsync(filePath);

        entropy.ShouldNotBeNull();
        entropy!.Value.ShouldBeGreaterThan(7.8);
    }

    [Fact]
    public async Task ComputeFileEntropy_EmptyFile_ReturnsZero()
    {
        string filePath = Path.Combine(_testDir, "empty.txt");
        await File.WriteAllBytesAsync(filePath, []);

        double? entropy = await _calculator.ComputeFileEntropyAsync(filePath);

        entropy.ShouldNotBeNull();
        entropy!.Value.ShouldBe(0.0);
    }

    [Fact]
    public async Task ComputeFileEntropy_NonExistentFile_ReturnsNull()
    {
        double? entropy = await _calculator.ComputeFileEntropyAsync(
            Path.Combine(_testDir, "nonexistent.txt"));

        entropy.ShouldBeNull();
    }

    [Fact]
    public void ComputeEntropy_AlwaysInRange_ZeroToEight()
    {
        // Property-like test: random inputs always produce [0, 8]
        for (int i = 0; i < 100; i++)
        {
            byte[] data = RandomNumberGenerator.GetBytes(RandomNumberGenerator.GetInt32(1, 10000));
            double entropy = _calculator.ComputeEntropy(data);
            entropy.ShouldBeGreaterThanOrEqualTo(0.0);
            entropy.ShouldBeLessThanOrEqualTo(8.0);
        }
    }

    [Fact]
    public async Task ComputeFileEntropy_LargeFile_UsesSampling()
    {
        // Create a 2 MB file — should use sampling strategy
        string filePath = Path.Combine(_testDir, "large.bin");
        byte[] data = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        await File.WriteAllBytesAsync(filePath, data);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double? entropy = await _calculator.ComputeFileEntropyAsync(filePath);
        sw.Stop();

        entropy.ShouldNotBeNull();
        entropy!.Value.ShouldBeGreaterThan(7.5);
        // Sampling should make this fast (< 200ms even on slow disk)
        sw.ElapsedMilliseconds.ShouldBeLessThan(500);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
