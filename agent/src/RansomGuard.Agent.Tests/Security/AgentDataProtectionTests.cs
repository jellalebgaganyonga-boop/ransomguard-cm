using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Security;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// AGT-KEY-001: the agent's data directories are closed to SYSTEM and Administrators (plus the
/// developer's account in Development), verified after the attempt, and an open tree that cannot
/// be closed refuses the start.
/// </summary>
/// <remarks>
/// These tests work in a temporary directory, never in %ProgramData%. They grant the test's own
/// account full control (the Development policy) so the directory can be cleaned up without
/// elevation; the Production policy itself is checked separately.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AgentDataProtectionTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rg-dataprot-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _cleanup = [];

    public AgentDataProtectionTests()
    {
        Directory.CreateDirectory(_root);
        _cleanup.Add(_root);
    }

    private static DaclEnforcer Enforcer() => new(NullLogger<DaclEnforcer>.Instance);

    // ── An open DACL is closed ──

    [Fact]
    public void An_open_tree_is_closed_inheritance_cut_and_verified()
    {
        string keys = Directory.CreateDirectory(Path.Combine(_root, "keys")).FullName;
        string key = Path.Combine(keys, "audit-signing.key");
        File.WriteAllText(key, "secret");
        Grant(new DirectoryInfo(_root), Everyone, FileSystemRights.ReadAndExecute, inherit: true);
        GrantFile(key, Users, FileSystemRights.Read);

        IReadOnlyList<string> problems = Enforcer().SecureDirectoryTree(_root, [Me]);

        problems.ShouldBeEmpty();
        new DirectoryInfo(_root).GetAccessControl().AreAccessRulesProtected.ShouldBeTrue();
        foreach (string entry in new[] { _root, keys, key })
        {
            Grantees(entry).ShouldBeSubsetOf([SystemSid, AdminsSid, Me]);
        }
        new FileInfo(key).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Count
            .ShouldBe(0, "a descendant keeps no explicit entry: it inherits from the closed root");
        Enforcer().VerifyDirectoryTree(_root, [Me]).ShouldBeEmpty();
    }

    // ── A DACL that cannot be corrected refuses the start ──

    [Fact]
    public void A_dacl_that_cannot_be_corrected_is_reported_never_assumed()
    {
        Grant(new DirectoryInfo(_root), Everyone, FileSystemRights.ReadAndExecute, inherit: true);
        var cannotWrite = new DaclEnforcer(NullLogger<DaclEnforcer>.Instance, (_, _) => { });

        IReadOnlyList<string> problems = cannotWrite.SecureDirectoryTree(_root, [Me]);

        problems.ShouldContain(p => p.Contains("still inherits permissions from its parent"));
        problems.ShouldContain(p => p.Contains("grants") && p.Contains(Everyone.Value));
    }

    [Fact]
    public void Protect_returns_errors_for_an_open_tree_the_agent_cannot_close()
    {
        Grant(new DirectoryInfo(_root), Everyone, FileSystemRights.ReadAndExecute, inherit: true);
        var cannotWrite = new DaclEnforcer(NullLogger<DaclEnforcer>.Instance, (_, _) => { });

        DataProtectionResult result = AgentDataProtection.Protect(
            ConfigUnder(_root), DataProtectionPolicy.For("Development", Me), cannotWrite, _root);

        // Program.ProtectAgentData refuses the start on any error (exit code 1).
        result.Errors.ShouldNotBeEmpty();
        result.Errors.ShouldAllBe(e => e.StartsWith(_root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Protect_closes_the_whole_data_root_database_keys_and_logs()
    {
        DataProtectionResult result = AgentDataProtection.Protect(
            ConfigUnder(_root), DataProtectionPolicy.For("Development", Me), Enforcer(), _root);

        result.Errors.ShouldBeEmpty();
        result.Roots.ShouldBe([Path.GetFullPath(_root)]);
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        File.WriteAllText(Path.Combine(_root, "data", "agent.db"), "db");
        Grantees(Path.Combine(_root, "data", "agent.db")).ShouldBeSubsetOf([SystemSid, AdminsSid, Me]);
    }

    // ── Development / Production ──

    [Fact]
    public void In_development_the_current_account_is_granted()
    {
        DataProtectionPolicy policy = DataProtectionPolicy.For("Development", Me);
        policy.AdditionalFullControl.ShouldBe([Me]);
        policy.AddsDevelopmentAccount.ShouldBeTrue();

        AgentDataProtection.Protect(ConfigUnder(_root), policy, Enforcer(), _root).Errors.ShouldBeEmpty();
        Grantees(_root).ShouldContain(Me);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void Outside_development_only_system_and_administrators(string? environment)
    {
        DataProtectionPolicy policy = DataProtectionPolicy.For(environment, Me);
        policy.AdditionalFullControl.ShouldBeEmpty();
        policy.AddsDevelopmentAccount.ShouldBeFalse();
    }

    // ── Never redirected ──

    [Fact]
    public void A_junction_inside_the_tree_is_reported_and_never_followed()
    {
        string outside = Path.Combine(Path.GetTempPath(), "rg-dataprot-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        _cleanup.Add(outside);
        Grant(new DirectoryInfo(outside), Everyone, FileSystemRights.ReadAndExecute, inherit: true);
        string link = Path.Combine(_root, "keys");
        RunMklinkJunction(link, outside);

        IReadOnlyList<string> problems = Enforcer().SecureDirectoryTree(_root, [Me]);

        problems.ShouldContain(p => p.StartsWith(link, StringComparison.OrdinalIgnoreCase) && p.Contains("reparse point"));
        Grantees(outside).ShouldContain(Everyone, "the junction's target must be left untouched");
        Directory.Delete(link); // the junction only, not its target
    }

    // ── Which directories ──

    [Fact]
    public void Default_paths_give_one_root()
    {
        (IReadOnlyList<string> roots, IReadOnlyList<string> errors) = AgentDataProtection.ResolveRoots(ConfigUnder(_root), _root);
        errors.ShouldBeEmpty();
        roots.ShouldBe([Path.GetFullPath(_root)]);
    }

    [Fact]
    public void A_dedicated_directory_outside_the_root_is_closed_too()
    {
        // Outside the data root and outside any profile (%TEMP% is in one, and is refused).
        // Only resolved, never created.
        string logs = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "rg-dataprot-logs-" + Guid.NewGuid().ToString("N"));
        AgentConfiguration config = ConfigUnder(_root, ("Agent:Logging:LogFilePath", Path.Combine(logs, "agent-.log")));

        (IReadOnlyList<string> roots, IReadOnlyList<string> errors) = AgentDataProtection.ResolveRoots(config, _root);

        errors.ShouldBeEmpty();
        roots.ShouldBe([Path.GetFullPath(_root), Path.GetFullPath(logs)]);
    }

    [Theory]
    [InlineData("Agent:Logging:LogFilePath", @"C:\agent-.log")]
    [InlineData("Agent:Database:KeyDirectory", @"C:\Windows\Temp\rg-keys")]
    [InlineData("Agent:Database:KeyDirectory", @"C:\ProgramData")]
    [InlineData("Agent:Database:KeyDirectory", @"C:\Program Files")]
    [InlineData("Agent:Database:KeyDirectory", @"%PUBLIC%\..\SomeUser\Documents")]
    [InlineData("Agent:Database:KeyDirectory", @"%TEMP%\rg-keys")]
    public void A_shared_or_system_folder_is_refused_never_closed(string setting, string path)
    {
        (_, IReadOnlyList<string> errors) = AgentDataProtection.ResolveRoots(ConfigUnder(_root, (setting, path)), _root);

        errors.ShouldHaveSingleItem().ShouldStartWith(setting);
    }

    public void Dispose()
    {
        foreach (string directory in _cleanup)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary directory does not fail the test.
            }
        }
    }

    // ── Helpers ──

    private static AgentConfiguration ConfigUnder(string root, params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Agent:Identity:Id"] = "auto-generated-on-first-run",
            ["Agent:Identity:Hostname"] = "AUTO",
            ["Agent:Detection:WatchPaths:0"] = @"C:\Temp",
            ["Agent:Server:BaseUrl"] = "https://grid.example.org",
            ["Agent:Database:ConnectionString"] = $"Data Source={Path.Combine(root, "data", "agent.db")}",
            ["Agent:Database:KeyDirectory"] = Path.Combine(root, "keys"),
            ["Agent:Logging:LogFilePath"] = Path.Combine(root, "logs", "agent-.log"),
        };
        foreach ((string key, string value) in overrides)
        {
            settings[key] = value;
        }

        AgentConfigurationLoadResult load = AgentConfigurationLoader.Load(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "Production");
        load.Errors.ShouldBeEmpty();
        return load.Configuration!;
    }

    private static List<SecurityIdentifier> Grantees(string path)
    {
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .Distinct()
            .ToList();
    }

    private static void Grant(DirectoryInfo directory, SecurityIdentifier sid, FileSystemRights rights, bool inherit)
    {
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            sid, rights,
            inherit ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static void GrantFile(string path, SecurityIdentifier sid, FileSystemRights rights)
    {
        var file = new FileInfo(path);
        FileSecurity security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(sid, rights, AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    private static void RunMklinkJunction(string link, string target)
    {
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        mklink.ExitCode.ShouldBe(0, mklink.StandardError.ReadToEnd());
    }
}
