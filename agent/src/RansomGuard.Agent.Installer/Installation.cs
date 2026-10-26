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
            installation.Say($"FAILED: {ex.Message}");
            installation.Say("Nothing else was changed after this step. Details in " + Path.Combine(DataDir, "logs", "install.log"));
            log.WriteLine(ex.ToString());
            return 4;
        }
    }

    private int Execute()
    {
        Say($"[1/6] Checking the package ({_payload.ZipLength:N0} bytes)");
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
                    throw new InvalidDataException($"the package does not match its SHA-256 ({actual} != {_config.AgentPackageSha256})");
                }

                Say($"      package SHA-256 verified: {actual}");
            }

            Say($"[2/6] Existing service");
            StopAndDeleteExistingService();

            Say($"[3/6] Extracting into {InstallDir}");
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
                throw new InvalidDataException("the package does not contain RansomGuard.Agent.Service.exe and appsettings.json");
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

            Say($"      GRID {_config.GridBaseUrl}, workstation {Environment.MachineName}, data {DataDir}");

            Say("[5/6] The GRID's certificate authority");
            if (string.IsNullOrWhiteSpace(_config.GridCaPem))
            {
                throw new InvalidDataException("the payload does not contain the GRID certificate authority: the agent could not trust it");
            }

            using (X509Certificate2 ca = CaTrust.Parse(_config.GridCaPem))
            {
                bool added = CaTrust.EnsureTrusted(ca, StoreLocation.LocalMachine);
                Say($"      {ca.Subject}, thumbprint {ca.Thumbprint}: {(added ? "added to the machine root store" : "already present, nothing added")}");
            }

            Say("[6/6] Windows service");
            // sc.exe wants "key=" and its value as two tokens; ArgumentList quotes a value with
            // spaces (the path, the display name) by itself. A single "binPath= \"…\"" token gets
            // re-quoted and sc misreads every field ("Invalid start= field", CI run 39658445948).
            Sc("create", ServiceName, "binPath=", exe, "start=", "auto", "DisplayName=", "RansomGuard-CM Agent", "obj=", "LocalSystem");
            Sc("description", ServiceName, "RansomGuard-CM endpoint detection and response agent");
            Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/120000/restart/300000");
            StartServiceWithOneRetry();
            Say("      Service started");

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
            Say("      No existing service");
            return;
        }

        Say($"      Service {ServiceName} present ({existing.Status}): stopping and removing");
        if (existing.Status != ServiceControllerStatus.Stopped)
        {
            try
            {
                existing.Stop();
                existing.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                Say($"      Stop not confirmed ({ex.Message}); removal requested anyway");
            }
        }

        Sc("delete", ServiceName);
        Thread.Sleep(1000);
    }

    /// <summary>
    /// Starts the service, and if the Service Control Manager loses patience, gives it one more go.
    ///
    /// <para>
    /// `sc start` fails with 1053 when the service has not reported itself running inside the
    /// SCM's limit, which is 30 s and is not ours to set. That is not the same as the service
    /// having failed: it may be running a moment later. The agent was changed so that nothing slow
    /// happens before Windows is told it is running, and this is the belt to that braces -- an
    /// installation must not be lost to one slow start on a busy workstation.
    /// </para>
    ///
    /// <para>
    /// Both the first attempt and the retry are judged by asking the service what state it is in,
    /// not by what sc.exe returned, because the service reaching Running is the thing that matters.
    /// </para>
    /// </summary>
    private void StartServiceWithOneRetry()
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Sc("start", ServiceName);
            }
            catch (InvalidOperationException ex)
            {
                // Includes 1053. Say it, then let the wait below decide: the service may well be
                // on its way up.
                Say($"      Start not confirmed by sc.exe ({ex.Message.Trim()})");
            }

            if (WaitForRunning(ServiceStartTimeout))
            {
                return;
            }

            if (attempt == 1)
            {
                Say($"      Not running after {ServiceStartTimeout.TotalSeconds:N0} s; starting it once more");
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }

        using var service = new ServiceController(ServiceName);
        service.Refresh();
        throw new InvalidOperationException(
            $"the service did not reach Running after two attempts (it is {service.Status}). " +
            $"See {Path.Combine(DataDir, "logs")}.");
    }

    /// <summary>How long to let the service take to reach Running, per attempt.</summary>
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Whether the service reaches Running within <paramref name="timeout"/>.</summary>
    private static bool WaitForRunning(TimeSpan timeout)
    {
        using var service = new ServiceController(ServiceName);
        try
        {
            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }

    // The agent writes enrollment.json beside its database once the GRID accepted it.
    private int WaitForEnrollment()
    {
        string stateFile = Path.Combine(DataDir, "data", "enrollment.json");
        DateTime deadline = DateTime.UtcNow + EnrollmentWait;
        Say($"      Enrolling with {_config.GridBaseUrl} ({(int)EnrollmentWait.TotalSeconds} s at most)...");
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(stateFile))
            {
                try
                {
                    using JsonDocument state = JsonDocument.Parse(File.ReadAllText(stateFile));
                    if (state.RootElement.TryGetProperty("AgentId", out JsonElement id) && !string.IsNullOrEmpty(id.GetString()))
                    {
                        Say($"Workstation enrolled (agent {id.GetString()}). It appears in the console within a minute.");
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
                    Say($"The service stopped ({service.Status}). See {Path.Combine(DataDir, "logs")}.");
                    return 4;
                }
            }

            Thread.Sleep(2000);
        }

        Say($"The workstation is not enrolled after {(int)EnrollmentWait.TotalSeconds} s: can this workstation reach the GRID {_config.GridBaseUrl}, and is the code still valid?");
        Say($"The service is running and will retry. Agent log: {Path.Combine(DataDir, "logs")}.");
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
            throw new InvalidOperationException($"sc.exe {arguments[0]} {arguments.ElementAtOrDefault(1)} failed ({process.ExitCode}): {output.Trim()}");
        }
    }

    private void Say(string line)
    {
        Console.WriteLine(line);
        _log.WriteLine($"{DateTime.UtcNow:o} {line}");
        _log.Flush();
    }
}
