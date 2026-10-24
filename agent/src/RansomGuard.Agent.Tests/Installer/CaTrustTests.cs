using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RansomGuard.Agent.Installer;
using Shouldly;

namespace RansomGuard.Agent.Tests.Installer;

/// <summary>
/// ADR-027, step 3: the GRID CA of the payload is a certificate, found by thumbprint. The write to
/// the machine root store is proven by the agent-full installer job, on a real machine store.
/// </summary>
public sealed class CaTrustTests
{
    [Fact]
    public void A_PEM_certificate_is_parsed_and_identified_by_its_thumbprint()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=RansomGuard-CM Test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string pem = issued.ExportCertificatePem();

        using X509Certificate2 parsed = CaTrust.Parse(pem);

        parsed.Thumbprint.ShouldBe(issued.Thumbprint);
        parsed.HasPrivateKey.ShouldBeFalse(); // the public certificate only: the payload never carries a key
        CaTrust.IsTrusted(parsed, StoreLocation.CurrentUser).ShouldBeFalse(); // a throwaway CA is in no store
    }

    [Fact]
    public void Something_that_is_not_a_certificate_is_refused_with_a_clear_message()
    {
        Should.Throw<InvalidDataException>(() => CaTrust.Parse("-----BEGIN CERTIFICATE-----\nnot a certificate\n-----END CERTIFICATE-----"))
            .Message.ShouldContain("not a PEM certificate");
    }
}
