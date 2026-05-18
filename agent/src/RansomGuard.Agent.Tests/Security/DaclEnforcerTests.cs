using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Security;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// Tests for <see cref="DaclEnforcer"/> DACL enforcement on files and directories.
/// Note: Full DACL verification requires Administrator privileges.
/// Tests that run as non-admin verify graceful degradation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DaclEnforcerTests : IDisposable
{
    private readonly DaclEnforcer _enforcer;
    private readonly string _testDir;

    public DaclEnforcerTests()
    {
        _enforcer = new DaclEnforcer(new Mock<ILogger<DaclEnforcer>>().Object);
        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_dacl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void EnforceOnFile_should_not_throw_for_existing_file()
    {
        string filePath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(filePath, "test data");

        // Should not throw even if not admin (logs warning instead)
        Should.NotThrow(() => _enforcer.EnforceOnFile(filePath));
    }

    [Fact]
    public void EnforceOnFile_should_skip_nonexistent_file()
    {
        string filePath = Path.Combine(_testDir, "nonexistent.db");

        Should.NotThrow(() => _enforcer.EnforceOnFile(filePath));
    }

    [Fact]
    public void EnforceOnDirectory_should_not_throw_for_existing_directory()
    {
        string subDir = Path.Combine(_testDir, "keys");
        Directory.CreateDirectory(subDir);

        Should.NotThrow(() => _enforcer.EnforceOnDirectory(subDir));
    }

    [Fact]
    public void EnforceOnDirectory_should_skip_nonexistent_directory()
    {
        string subDir = Path.Combine(_testDir, "nonexistent");

        Should.NotThrow(() => _enforcer.EnforceOnDirectory(subDir));
    }

    [Fact]
    public void EnforceOnFile_with_admin_read_only_should_not_throw()
    {
        string filePath = Path.Combine(_testDir, "config.json");
        File.WriteAllText(filePath, "{}");

        Should.NotThrow(() => _enforcer.EnforceOnFile(filePath, allowAdminReadOnly: true));
    }

    [Fact]
    public void Enforced_file_dacl_should_contain_system_and_admin_rules()
    {
        string filePath = Path.Combine(_testDir, "readable.txt");
        File.WriteAllText(filePath, "test content");

        _enforcer.EnforceOnFile(filePath);

        // Verify DACL has SYSTEM and Administrators rules
        var fileInfo = new FileInfo(filePath);
        FileSecurity security = fileInfo.GetAccessControl();
        AuthorizationRuleCollection rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        bool hasSystem = false;
        bool hasAdmin = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference.Equals(systemSid)) hasSystem = true;
            if (rule.IdentityReference.Equals(adminSid)) hasAdmin = true;
        }

        hasSystem.ShouldBeTrue();
        hasAdmin.ShouldBeTrue();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
