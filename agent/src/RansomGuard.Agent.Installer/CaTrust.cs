using System.Security.Cryptography.X509Certificates;

namespace RansomGuard.Agent.Installer;

/// <summary>
/// The GRID certificate authority in the machine's root store (ADR-027, 2c, point 4): added once,
/// only that certificate, said in the log. Without it the agent would not trust the GRID on the
/// local network (AGT-PKI-001) and would never enroll.
/// </summary>
public static class CaTrust
{
    /// <summary>Parses the PEM of the payload; throws with a clear message when it is not a certificate.</summary>
    public static X509Certificate2 Parse(string pem)
    {
        try
        {
            return X509Certificate2.CreateFromPem(pem);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("the GRID certificate authority in the payload is not a PEM certificate", ex);
        }
    }

    /// <summary>True when a certificate with this thumbprint is already in the store.</summary>
    public static bool IsTrusted(X509Certificate2 ca, StoreLocation location = StoreLocation.LocalMachine)
    {
        using var store = new X509Store(StoreName.Root, location);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, validOnly: false).Count > 0;
    }

    /// <summary>Adds the certificate to the root store unless it is there already. Returns true when added.</summary>
    public static bool EnsureTrusted(X509Certificate2 ca, StoreLocation location = StoreLocation.LocalMachine)
    {
        using var store = new X509Store(StoreName.Root, location);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, validOnly: false).Count > 0)
        {
            return false;
        }

        store.Add(ca);
        return true;
    }
}
