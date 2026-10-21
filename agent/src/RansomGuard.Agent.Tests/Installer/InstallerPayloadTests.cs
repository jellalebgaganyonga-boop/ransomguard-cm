using System.Text;
using RansomGuard.Agent.Installer;
using Shouldly;

namespace RansomGuard.Agent.Tests.Installer;

/// <summary>
/// ADR-027, step 1: the payload the GRID appends to the stub, read back by the stub. A bad file
/// never throws: it says what is wrong, so the person at the keyboard knows to download again.
/// </summary>
public sealed class InstallerPayloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rg-installer-").FullName;
    private readonly string _stub;
    private readonly byte[] _zip = Encoding.ASCII.GetBytes("PK\u0003\u0004 fake agent package " + new string('z', 5000));
    private readonly InstallerConfig _config = new()
    {
        GridBaseUrl = "https://grid.hopital.local:8443",
        Otp = "ABCDEFGHIJKL",
        OtpExpiresAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero),
        TenantCode = "hopital-yde",
        AgentPackageSha256 = new string('a', 64),
        GridCaPem = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n",
    };

    public InstallerPayloadTests()
    {
        // The "stub": any bytes; the real one is the published executable, proven by the CI.
        _stub = Path.Combine(_dir, "stub.exe");
        File.WriteAllBytes(_stub, Encoding.ASCII.GetBytes("MZ stub bytes " + new string('s', 1000)));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Installer(Action<byte[]>? tamper = null)
    {
        string path = Path.Combine(_dir, $"installer-{Guid.NewGuid():N}.exe");
        InstallerPayload.Append(_stub, _zip, _config, path);
        if (tamper is not null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            tamper(bytes);
            File.WriteAllBytes(path, bytes);
        }

        return path;
    }

    [Fact]
    public void A_payload_appended_to_the_stub_is_read_back_whole()
    {
        string installer = Installer();

        PayloadReadResult payload = InstallerPayload.Read(installer);

        payload.IsPresent.ShouldBeTrue(payload.Problem);
        payload.Config!.GridBaseUrl.ShouldBe(_config.GridBaseUrl);
        payload.Config.Otp.ShouldBe(_config.Otp);
        payload.Config.OtpExpiresAt.ShouldBe(_config.OtpExpiresAt);
        payload.Config.TenantCode.ShouldBe("hopital-yde");
        payload.Config.GridCaPem.ShouldBe(_config.GridCaPem);
        payload.ZipLength.ShouldBe(_zip.Length);
        payload.ZipOffset.ShouldBe(new FileInfo(_stub).Length); // the zip starts right after the stub
        long configLength = new FileInfo(installer).Length - new FileInfo(_stub).Length - _zip.Length - InstallerPayload.TrailerSize;
        configLength.ShouldBeInRange(100, InstallerPayload.MaxConfigLength); // the JSON, nothing else

        string extracted = Path.Combine(_dir, "agent.zip");
        InstallerPayload.ExtractZip(installer, payload, extracted);
        File.ReadAllBytes(extracted).ShouldBe(_zip);
    }

    [Fact]
    public void The_bare_stub_has_no_payload_and_says_so()
    {
        PayloadReadResult payload = InstallerPayload.Read(_stub);

        payload.IsPresent.ShouldBeFalse();
        payload.IsCorrupt.ShouldBeFalse();
        payload.Problem!.ShouldContain("bare stub");
    }

    [Fact]
    public void A_byte_changed_in_the_package_is_refused_download_again()
    {
        string installer = Installer(bytes => bytes[new FileInfo(_stub).Length + 100] ^= 0xFF);

        PayloadReadResult payload = InstallerPayload.Read(installer);

        payload.IsPresent.ShouldBeFalse();
        payload.IsCorrupt.ShouldBeTrue();
        payload.Problem!.ShouldContain("SHA-256");
    }

    [Fact]
    public void A_truncated_download_is_refused_not_misread()
    {
        string installer = Installer();
        byte[] bytes = File.ReadAllBytes(installer);
        File.WriteAllBytes(installer, bytes[..^(InstallerPayload.TrailerSize + 10)]); // the trailer is gone with the end

        InstallerPayload.Read(installer).IsPresent.ShouldBeFalse();

        // The trailer kept but a length pointing before the file start: refused, not an exception.
        string lying = Installer(b =>
        {
            long huge = long.MaxValue / 4;
            BitConverter.GetBytes(huge).CopyTo(b, b.Length - InstallerPayload.TrailerSize + 8);
        });
        PayloadReadResult payload = InstallerPayload.Read(lying);
        payload.IsCorrupt.ShouldBeTrue();
        payload.Problem!.ShouldContain("out of range");
    }

    [Fact]
    public void A_config_without_the_GRID_address_or_the_code_is_refused()
    {
        string path = Path.Combine(_dir, "no-otp.exe");
        InstallerPayload.Append(_stub, _zip, _config with { Otp = " " }, path);

        PayloadReadResult payload = InstallerPayload.Read(path);

        payload.IsCorrupt.ShouldBeTrue();
        payload.Problem!.ShouldContain("enrollment code");
    }

    [Fact]
    public void The_trailer_is_56_bytes_magic_lengths_and_hash()
    {
        byte[] trailer = InstallerPayload.Trailer(_zip, Encoding.UTF8.GetBytes("{}"));

        trailer.Length.ShouldBe(56);
        Encoding.ASCII.GetString(trailer, 0, 8).ShouldBe("RGINST01");
        BitConverter.ToInt64(trailer, 8).ShouldBe(_zip.Length);
        BitConverter.ToInt64(trailer, 16).ShouldBe(2);
    }
}
