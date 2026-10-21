using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text.Json;

namespace RansomGuard.Agent.Installer;

/// <summary>
/// What the double-click does (ADR-027, 2c): the same steps as <c>install.ps1</c>, with the package
/// and the enrollment data taken from the payload instead of the network. Every step is printed
/// and written to <c>C:\ProgramData\RansomGuard-CM\logs\install.log</c>; a failure stops before the
/// next step and says which one.
/// </summary>
public sealed class Installation
{
    /// <summary>The production service, the one <c>install.ps1</c> creates.</summary>
    public const string ServiceName = "RansomGuardAgent";

    /// <summary>Under Program Files only (AGT-INST-001): users cannot replace what runs as LocalSystem.</summary>
    public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RansomGuard-CM");

    /// <summary>The agent's data root; the agent closes it to SYSTEM and Administrators at start (AGT-KEY-001).</summary>
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RansomGuard-CM");

    /// <summary>How long the installer waits for the agent to enroll before saying it did not.</summary>
    public static readonly TimeSpan EnrollmentWait = TimeSpan.FromMinutes(2);

    private readonly string _self;
    private readonly PayloadReadResult _payload;
    private readonly InstallerConfig _config;
    private readonly TextWriter _log;

    private Installation(string self, PayloadReadResult payload, TextWriter log)
    {
        _self = self;
        _payload = payload;
        _config = payload.Config!;
        _log = log;
    }

    /// <summary>Runs the installation; returns the process exit code (0 enrolled, 2 payload, 3 code expired, 4 not enrolled).</summary>
    public static int Run(string self, PayloadReadResult payload)
    {
        Directory.CreateDirectory(Path.Combine(DataDir, "logs"));
        using var log = new StreamWriter(Path.Combine(DataDir, "logs", "install.log"), append: true);
        var installation = new Installation(self, payload, log);
        try
        {
            return installation.Execute();
        }
        catch (Exception ex)
        {
            installation.Say($"ÉCHEC : {ex.Message}");
            installation.Say("Rien d'autre n'a été modifié après cette étape. Détail dans " + Path.Combine(DataDir, "logs", "install.log"));
            log.WriteLine(ex.ToString());
            return 4;
        }
    }

    private int Execute()
    {
        Say($"[1/6] Vérification du paquet ({_payload.ZipLength:N0} octets)");
        string tempZip = Path.Combine(Path.GetTempPath(), $"RansomGuard-Agent-{Guid.NewGuid():N}.zip");
        try
        {
            InstallerPayload.ExtractZip(_self, _payload, tempZip);
            if (!string.IsNullOrEmpty(_config.AgentPackageSha256))
            {
                using FileStream zip = File.OpenRead(tempZip);
                string actual = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
                if (!actual.Equals(_config.AgentPackageSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"le paquet ne correspond pas à son SHA-256 ({actual} ≠ {_config.AgentPackageSha256})");
                }

                Say($"      SHA-256 du paquet vérifié : {actual}");
            }

            Say($"[2/6] Service existant");
            StopAndDeleteExistingService();

            Say($"[3/6] Extraction dans {InstallDir}");
            if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, recursive: true);
            }

            Directory.CreateDirectory(InstallDir);
            ZipFile.ExtractToDirectory(tempZip, InstallDir);
            string exe = Path.Combine(InstallDir, "RansomGuard.Agent.Service.exe");
            string settingsPath = Path.Combine(InstallDir, "appsettings.json");
            if (!File.Exists(exe) || !File.Exists(settingsPath))
            {
                throw new InvalidDataException("le paquet ne contient pas RansomGuard.Agent.Service.exe et appsettings.json");
            }

            string version = FileVersionInfo.GetVersionInfo(Path.Combine(InstallDir, "RansomGuard.Agent.Service.dll")).ProductVersion ?? "inconnue";
            Say($"      Agent {version}");

            Say("[4/6] Configuration");
            File.WriteAllText(settingsPath, AgentSettings.Apply(
                File.ReadAllText(settingsPath), _config.GridBaseUrl, _config.Otp, Environment.MachineName, DataDir));
            foreach (string sub in new[] { "data", "logs", "keys" })
            {
                Directory.CreateDirectory(Path.Combine(DataDir, sub));
            }

            Say($"      GRID {_config.GridBaseUrl}, poste {Environment.MachineName}, données {DataDir}");

            Say("[5/6] Autorité de certification du GRID");
            if (string.IsNullOrWhiteSpace(_config.GridCaPem))
            {
                throw new InvalidDataException("la charge utile ne contient pas le certificat de l'autorité du GRID : l'agent ne pourrait pas lui faire confiance");
            }

            using (X509Certificate2 ca = CaTrust.Parse(_config.GridCaPem))
            {
                bool added = CaTrust.EnsureTrusted(ca, StoreLocation.LocalMachine);
                Say($"      {ca.Subject}, empreinte {ca.Thumbprint} : {(added ? "ajoutée au magasin racine de la machine" : "déjà présente, rien ajouté")}");
            }

            Say("[6/6] Service Windows");
            // sc.exe wants "key=" and its value as two tokens; ArgumentList quotes a value with
            // spaces (the path, the display name) by itself. A single "binPath= \"…\"" token gets
            // re-quoted and sc misreads every field ("Invalid start= field", CI run 39658445948).
            Sc("create", ServiceName, "binPath=", exe, "start=", "auto", "DisplayName=", "RansomGuard-CM Agent", "obj=", "LocalSystem");
            Sc("description", ServiceName, "RansomGuard-CM endpoint detection and response agent");
            Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/120000/restart/300000");
            Sc("start", ServiceName);
            WaitForService(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
            Say("      Service démarré");

            return WaitForEnrollment();
        }
        finally
        {
            try
            {
                File.Delete(tempZip);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private void StopAndDeleteExistingService()
    {
        using var existing = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Say("      Aucun service existant");
            return;
        }

        Say($"      Service {ServiceName} présent ({existing.Status}) : arrêt et suppression");
        if (existing.Status != ServiceControllerStatus.Stopped)
        {
            try
            {
                existing.Stop();
                existing.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                Say($"      Arrêt non confirmé ({ex.Message}) ; suppression demandée quand même");
            }
        }

        Sc("delete", ServiceName);
        Thread.Sleep(1000);
    }

    private void WaitForService(ServiceControllerStatus status, TimeSpan timeout)
    {
        using var service = new ServiceController(ServiceName);
        service.WaitForStatus(status, timeout);
    }

    // The agent writes enrollment.json beside its database once the GRID accepted it.
    private int WaitForEnrollment()
    {
        string stateFile = Path.Combine(DataDir, "data", "enrollment.json");
        DateTime deadline = DateTime.UtcNow + EnrollmentWait;
        Say($"      Enrôlement auprès de {_config.GridBaseUrl} ({(int)EnrollmentWait.TotalSeconds} s au plus)…");
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(stateFile))
            {
                try
                {
                    using JsonDocument state = JsonDocument.Parse(File.ReadAllText(stateFile));
                    if (state.RootElement.TryGetProperty("AgentId", out JsonElement id) && !string.IsNullOrEmpty(id.GetString()))
                    {
                        Say($"Poste enrôlé (agent {id.GetString()}). Il apparaît dans la console sous une minute.");
                        return 0;
                    }
                }
                catch (JsonException)
                {
                    // being written: read again
                }
            }

            using (var service = new ServiceController(ServiceName))
            {
                if (service.Status != ServiceControllerStatus.Running)
                {
                    Say($"Le service s'est arrêté ({service.Status}). Voir {Path.Combine(DataDir, "logs")}.");
                    return 4;
                }
            }

            Thread.Sleep(2000);
        }

        Say($"Le poste n'est pas enrôlé après {(int)EnrollmentWait.TotalSeconds} s : le GRID {_config.GridBaseUrl} est-il joignable depuis ce poste, le code est-il encore valide ?");
        Say($"Le service tourne et réessaiera. Journal de l'agent : {Path.Combine(DataDir, "logs")}.");
        return 4;
    }

    private void Sc(params string[] arguments)
    {
        var psi = new ProcessStartInfo("sc.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("sc.exe did not start");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        _log.WriteLine($"sc.exe {string.Join(' ', arguments)} -> {process.ExitCode}: {output.Trim()}");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc.exe {arguments[0]} {arguments.ElementAtOrDefault(1)} a échoué ({process.ExitCode}) : {output.Trim()}");
        }
    }

    private void Say(string line)
    {
        Console.WriteLine(line);
        _log.WriteLine($"{DateTime.UtcNow:o} {line}");
        _log.Flush();
    }
}
