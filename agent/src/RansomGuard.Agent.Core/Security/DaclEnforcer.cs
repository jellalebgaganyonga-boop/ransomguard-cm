using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Enforces DACL (Discretionary Access Control List) on sensitive RansomGuard files (CWE-732).
/// Restricts database, keys, and logs to SYSTEM and Administrators only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DaclEnforcer
{
    private readonly ILogger<DaclEnforcer> _logger;

    /// <summary>
    /// Initializes the DACL enforcer.
    /// </summary>
    public DaclEnforcer(ILogger<DaclEnforcer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enforces restricted DACL on a file: SYSTEM full control + Administrators full control.
    /// Removes all other access.
    /// </summary>
    /// <param name="filePath">Path to the file to secure.</param>
    /// <param name="allowAdminReadOnly">If true, Administrators get read-only instead of full control.</param>
    public void EnforceOnFile(string filePath, bool allowAdminReadOnly = false)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            FileSecurity security = fileInfo.GetAccessControl();

            // Remove all existing rules
            AuthorizationRuleCollection existingRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in existingRules)
            {
                security.RemoveAccessRule(rule);
            }

            // SYSTEM: Full Control
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // Administrators: Full Control or ReadAndExecute
            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            FileSystemRights adminRights = allowAdminReadOnly
                ? FileSystemRights.ReadAndExecute
                : FileSystemRights.FullControl;
            security.AddAccessRule(new FileSystemAccessRule(
                adminSid,
                adminRights,
                AccessControlType.Allow));

            // Disable inheritance
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            fileInfo.SetAccessControl(security);

            _logger.LogInformation("DACL enforced on {FilePath} (AdminReadOnly: {AdminReadOnly})",
                filePath, allowAdminReadOnly);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Cannot enforce DACL on {FilePath} — insufficient privileges. Will retry on next startup", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enforce DACL on {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Enforces restricted DACL on a directory and its contents.
    /// </summary>
    /// <param name="directoryPath">Path to the directory.</param>
    /// <param name="allowAdminReadOnly">If true, Administrators get read-only.</param>
    public void EnforceOnDirectory(string directoryPath, bool allowAdminReadOnly = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            DirectorySecurity security = dirInfo.GetAccessControl();

            AuthorizationRuleCollection existingRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in existingRules)
            {
                security.RemoveAccessRule(rule);
            }

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            FileSystemRights adminRights = allowAdminReadOnly
                ? FileSystemRights.ReadAndExecute
                : FileSystemRights.FullControl;
            security.AddAccessRule(new FileSystemAccessRule(
                adminSid,
                adminRights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            dirInfo.SetAccessControl(security);

            _logger.LogInformation("DACL enforced on directory {Directory} (AdminReadOnly: {AdminReadOnly})",
                directoryPath, allowAdminReadOnly);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Cannot enforce DACL on directory {Directory} — insufficient privileges", directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enforce DACL on directory {Directory}", directoryPath);
        }
    }
}
