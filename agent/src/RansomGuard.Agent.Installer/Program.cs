using System.Reflection;
using RansomGuard.Agent.Installer;

// The double-click installer (ADR-027). Step 1 of 5: the stub reads the payload the console
// appended to it. Exit codes are the contract of the acceptance test:
//   0 payload read (and, from step 3, the agent installed and enrolled)
//   2 payload absent or corrupt: "retéléchargez l'installeur"
//   3 enrollment code expired
//   4 the agent did not enroll (step 3)
//  64 bad usage
string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
string self = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");

switch (args.Length > 0 ? args[0] : "")
{
    case "--version":
        Console.WriteLine(version);
        return 0;

    case "--append":
        // Development and CI helper: the console does the same in Python.
        if (args.Length != 5)
        {
            Console.Error.WriteLine("usage: --append <stub.exe> <agent.zip> <config.json> <output.exe>");
            return 64;
        }

        InstallerConfig? config = System.Text.Json.JsonSerializer.Deserialize<InstallerConfig>(
            File.ReadAllBytes(args[3]), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
        if (config is null)
        {
            Console.Error.WriteLine("config.json: empty");
            return 64;
        }

        InstallerPayload.Append(args[1], File.ReadAllBytes(args[2]), config, args[4]);
        Console.WriteLine($"written {args[4]} ({new FileInfo(args[4]).Length} bytes)");
        return 0;

    case "--verify-payload":
    case "":
        return VerifyPayload(self, version, install: args.Length == 0);

    default:
        Console.Error.WriteLine("usage: RansomGuard-Installer.exe [--verify-payload | --version | --append ...]");
        return 64;
}

static int VerifyPayload(string self, string version, bool install)
{
    Console.WriteLine($"RansomGuard-CM — installeur {version}");
    PayloadReadResult payload = InstallerPayload.Read(self);
    if (!payload.IsPresent)
    {
        Console.Error.WriteLine(payload.IsCorrupt
            ? $"Installeur incomplet : {payload.Problem}. Retéléchargez-le depuis la console."
            : $"Pas de charge utile : {payload.Problem}.");
        return 2;
    }

    InstallerConfig c = payload.Config!;
    Console.WriteLine($"GRID            : {c.GridBaseUrl}");
    Console.WriteLine($"Établissement   : {c.TenantCode ?? "(non précisé)"}");
    Console.WriteLine($"Code            : {Mask(c.Otp)}, expire le {c.OtpExpiresAt?.ToString("u") ?? "(non précisé)"}");
    Console.WriteLine($"Paquet agent    : {payload.ZipLength:N0} octets, SHA-256 {c.AgentPackageSha256 ?? "(non précisé)"}");
    Console.WriteLine($"Autorité GRID   : {(string.IsNullOrEmpty(c.GridCaPem) ? "absente" : "présente")}");
    Console.WriteLine("Charge utile vérifiée (SHA-256).");

    if (c.OtpExpiresAt is DateTimeOffset expires && expires < DateTimeOffset.UtcNow)
    {
        Console.Error.WriteLine("Code expiré : générez-en un nouveau dans la console.");
        return 3;
    }

    if (install)
    {
        // Step 3 of ADR-027 brings the installation itself; said as such, never pretended.
        Console.Error.WriteLine("L'installation n'est pas encore disponible dans cette version de l'installeur (ADR-027, étape 3).");
        return 2;
    }

    return 0;
}

static string Mask(string otp) =>
    otp.Length <= 4 ? "****" : otp[..2] + new string('*', otp.Length - 4) + otp[^2..];
