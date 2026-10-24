using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RansomGuard.Agent.Installer;

/// <summary>
/// What the GRID appends to the stub (ADR-027, 2b), and how the stub reads it back from its own
/// file. Layout, from the end of the executable:
/// <code>
/// [stub bytes][zip: agent package][config: UTF-8 JSON][trailer, 56 bytes]
/// trailer = "RGINST01" (8 ASCII) | zip length (int64 LE) | config length (int64 LE) | SHA-256(zip ‖ config) (32)
/// </code>
/// The GRID writes it in Python (the same layout, see the README of this project); the stub and
/// the tests use this class.
/// </summary>
public static class InstallerPayload
{
    /// <summary>The trailer's magic: format version 01.</summary>
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("RGINST01");

    /// <summary>The trailer's size in bytes.</summary>
    public const int TrailerSize = 8 + 8 + 8 + 32;

    /// <summary>The configuration part is small by construction: a few strings and a PEM.</summary>
    public const long MaxConfigLength = 64 * 1024;

    /// <summary>Appends a payload to a copy of the stub: what the GRID does on every download.</summary>
    /// <param name="stub">The published stub.</param>
    /// <param name="zip">The agent package.</param>
    /// <param name="config">The installer configuration.</param>
    /// <param name="output">The installer to write.</param>
    public static void Append(string stub, byte[] zip, InstallerConfig config, string output)
    {
        byte[] configBytes = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        using FileStream outStream = File.Create(output);
        using (FileStream stubStream = File.OpenRead(stub))
        {
            stubStream.CopyTo(outStream);
        }

        outStream.Write(zip);
        outStream.Write(configBytes);
        outStream.Write(Trailer(zip, configBytes));
    }

    /// <summary>Builds the 56-byte trailer for a zip and a config.</summary>
    public static byte[] Trailer(ReadOnlySpan<byte> zip, ReadOnlySpan<byte> config)
    {
        byte[] trailer = new byte[TrailerSize];
        Magic.CopyTo(trailer, 0);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(8, 8), zip.Length);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(16, 8), config.Length);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(zip);
        sha.AppendData(config);
        sha.GetHashAndReset().CopyTo(trailer, 24);
        return trailer;
    }

    /// <summary>
    /// Reads the payload at the end of <paramref name="executable"/>. Never throws for a bad
    /// file: the outcome says what is wrong, in words the person at the keyboard can act on.
    /// </summary>
    public static PayloadReadResult Read(string executable)
    {
        using FileStream file = File.OpenRead(executable);
        if (file.Length < TrailerSize)
        {
            return PayloadReadResult.Absent("the file is smaller than a trailer");
        }

        byte[] trailer = new byte[TrailerSize];
        file.Seek(-TrailerSize, SeekOrigin.End);
        file.ReadExactly(trailer);
        if (!trailer.AsSpan(0, 8).SequenceEqual(Magic))
        {
            return PayloadReadResult.Absent("no payload: this is the bare stub, not an installer made by the console");
        }

        long zipLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(8, 8));
        long configLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(16, 8));
        long payloadLength = zipLength + configLength;
        if (zipLength <= 0 || configLength <= 0 || configLength > MaxConfigLength || payloadLength > file.Length - TrailerSize)
        {
            return PayloadReadResult.Corrupt($"trailer lengths out of range (zip {zipLength}, config {configLength}, file {file.Length})");
        }

        long payloadStart = file.Length - TrailerSize - payloadLength;
        file.Seek(payloadStart, SeekOrigin.Begin);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long remaining = payloadLength;
        while (remaining > 0)
        {
            int read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                return PayloadReadResult.Corrupt("the file ends before its payload");
            }

            sha.AppendData(buffer, 0, read);
            remaining -= read;
        }

        if (!sha.GetHashAndReset().AsSpan().SequenceEqual(trailer.AsSpan(24, 32)))
        {
            return PayloadReadResult.Corrupt("the payload does not match its SHA-256: the download is incomplete or the file was altered");
        }

        file.Seek(payloadStart + zipLength, SeekOrigin.Begin);
        byte[] configBytes = new byte[configLength];
        file.ReadExactly(configBytes);
        InstallerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<InstallerConfig>(configBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            return PayloadReadResult.Corrupt($"the configuration is not valid JSON: {ex.Message}");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.GridBaseUrl) || string.IsNullOrWhiteSpace(config.Otp))
        {
            return PayloadReadResult.Corrupt("the configuration lacks the GRID address or the enrollment code");
        }

        return PayloadReadResult.Ok(config, payloadStart, zipLength);
    }

    /// <summary>Copies the zip part of a verified payload into a file.</summary>
    public static void ExtractZip(string executable, PayloadReadResult payload, string destination)
    {
        using FileStream file = File.OpenRead(executable);
        using FileStream zip = File.Create(destination);
        file.Seek(payload.ZipOffset, SeekOrigin.Begin);
        byte[] buffer = new byte[81920];
        long remaining = payload.ZipLength;
        while (remaining > 0)
        {
            int read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("the file ends before its package");
            }

            zip.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

/// <summary>The installer's configuration, written by the GRID for one download (ADR-027, 2b).</summary>
public sealed record InstallerConfig
{
    /// <summary>The GRID's agent channel the agent enrolls with, e.g. https://grid.hopital.local:443.</summary>
    [JsonPropertyName("grid_base_url")]
    public required string GridBaseUrl { get; init; }

    /// <summary>The one-time enrollment code (30 min).</summary>
    [JsonPropertyName("otp")]
    public required string Otp { get; init; }

    /// <summary>When the code expires (UTC): the installer refuses to run past it.</summary>
    [JsonPropertyName("otp_expires_at")]
    public DateTimeOffset? OtpExpiresAt { get; init; }

    /// <summary>The hospital's tenant code, shown to the person installing.</summary>
    [JsonPropertyName("tenant_code")]
    public string? TenantCode { get; init; }

    /// <summary>SHA-256 (hex) of the embedded agent package, as the CD published it.</summary>
    [JsonPropertyName("agent_package_sha256")]
    public string? AgentPackageSha256 { get; init; }

    /// <summary>The GRID certificate authority's public certificate (PEM), added to the machine root store.</summary>
    [JsonPropertyName("grid_ca_pem")]
    public string? GridCaPem { get; init; }
}

/// <summary>What reading the payload came to.</summary>
public sealed record PayloadReadResult
{
    /// <summary>Present, verified and parsed.</summary>
    public bool IsPresent => Config is not null;

    /// <summary>Present but unusable (lengths, hash, JSON): a retry of the download is the answer.</summary>
    public bool IsCorrupt { get; private init; }

    /// <summary>Why it is absent or corrupt; null when present.</summary>
    public string? Problem { get; private init; }

    /// <summary>The configuration, when present.</summary>
    public InstallerConfig? Config { get; private init; }

    /// <summary>Where the zip starts in the executable.</summary>
    public long ZipOffset { get; private init; }

    /// <summary>The zip's length.</summary>
    public long ZipLength { get; private init; }

    internal static PayloadReadResult Ok(InstallerConfig config, long zipOffset, long zipLength) =>
        new() { Config = config, ZipOffset = zipOffset, ZipLength = zipLength };

    internal static PayloadReadResult Absent(string why) => new() { Problem = why };

    internal static PayloadReadResult Corrupt(string why) => new() { Problem = why, IsCorrupt = true };
}
