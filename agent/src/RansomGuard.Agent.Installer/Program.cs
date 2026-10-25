using System.Reflection;
using RansomGuard.Agent.Installer;

// The double-click installer (ADR-027). Step 1 of 5: the stub reads the payload the console
// appended to it. Exit codes are the contract of the acceptance test:
//   0 payload read; with no argument: the agent installed and enrolled
//   2 payload absent or corrupt: "download the installer again"
//   3 enrollment code expired
//   4 the installation failed, or the agent did not enroll within 2 min
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
    Console.WriteLine($"RansomGuard-CM — installer {version}");
    PayloadReadResult payload = InstallerPayload.Read(self);
    if (!payload.IsPresent)
    {
        Console.Error.WriteLine(payload.IsCorrupt
            ? $"Incomplete installer: {payload.Problem}. Download it again from the console."
            : $"No payload: {payload.Problem}.");
        return 2;
    }

    InstallerConfig c = payload.Config!;
    Console.WriteLine($"GRID            : {c.GridBaseUrl}");
    Console.WriteLine($"Organization    : {c.TenantCode ?? "(not given)"}");
    Console.WriteLine($"Code            : {Mask(c.Otp)}, expires {c.OtpExpiresAt?.ToString("u") ?? "(not given)"}");
    Console.WriteLine($"Agent package   : {payload.ZipLength:N0} bytes, SHA-256 {c.AgentPackageSha256 ?? "(not given)"}");
    Console.WriteLine($"GRID authority  : {(string.IsNullOrEmpty(c.GridCaPem) ? "absent" : "present")}");
    Console.WriteLine("Payload verified (SHA-256).");

    if (c.OtpExpiresAt is DateTimeOffset expires && expires < DateTimeOffset.UtcNow)
    {
        Console.Error.WriteLine("Code expired: generate a new one in the console.");
        return 3;
    }

    if (!install)
    {
        return 0;
    }

    Console.WriteLine();
    int code = Installation.Run(self, payload);
    Console.WriteLine();
    Console.WriteLine("Press any key to close.");
    if (!Console.IsInputRedirected)
    {
        Console.ReadKey(intercept: true);
    }

    return code;
}

static string Mask(string otp) =>
    otp.Length <= 4 ? "****" : otp[..2] + new string('*', otp.Length - 4) + otp[^2..];
