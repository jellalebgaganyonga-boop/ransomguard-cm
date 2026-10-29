using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Enforces DACL (Discretionary Access Control List) on sensitive RansomGuard files (CWE-732).
/// Restricts database, keys, and logs to SYSTEM and Administrators only.
/// <see cref="SecureDirectoryTree"/> is what the agent applies at startup (AGT-KEY-001); the two
/// single-entry methods below predate it and swallow their failures.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DaclEnforcer
{
    private readonly ILogger<DaclEnforcer> _logger;
    private readonly Action<FileSystemInfo, FileSystemSecurity> _persist;

    /// <summary>
    /// Initializes the DACL enforcer.
    /// </summary>
    public DaclEnforcer(ILogger<DaclEnforcer> logger)
        : this(logger, PersistAcl)
    {
    }

    /// <summary>Tests: replaces the write of a DACL (to simulate a DACL that cannot be corrected).</summary>
    internal DaclEnforcer(ILogger<DaclEnforcer> logger, Action<FileSystemInfo, FileSystemSecurity> persist)
    {
        _logger = logger;
        _persist = persist;
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

    // ── AGT-KEY-001: the agent's data directories, closed and verified ─────────────

    /// <summary>
    /// Closes a whole directory tree to SYSTEM, Administrators and <paramref name="additionalFullControl"/>
    /// (the developer's own account, in Development only), then verifies the result.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="EnforceOnDirectory"/>, nothing is swallowed: every failure, and every entry
    /// that is still open after the attempt, is returned. An empty list means the tree is closed.
    /// <list type="bullet">
    /// <item>The root gets a protected DACL (inheritance cut), inheritable full control for the allowed
    /// accounts only, and an allowed owner (Administrators when it was anyone else: an owner can
    /// always rewrite a DACL).</item>
    /// <item>Every descendant loses its explicit entries and inherits from the root again.</item>
    /// <item>A reparse point (junction, symbolic link) anywhere in the tree is never followed: it could
    /// redirect the agent's DACL onto another directory. It is reported instead.</item>
    /// </list>
    /// </remarks>
    /// <returns>The violations still present after the attempt; empty when the tree is closed.</returns>
    public IReadOnlyList<string> SecureDirectoryTree(string root, IReadOnlyCollection<SecurityIdentifier> additionalFullControl)
    {
        HashSet<SecurityIdentifier> allowed = AllowedAccounts(additionalFullControl);
        var failures = new List<string>();

        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists)
        {
            return [$"{root}: does not exist"];
        }

        if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return [$"{root}: is a reparse point (junction or symbolic link); refusing to follow it"];
        }

        try
        {
            // Explicit entries removed here; inherited ones go with the protection below.
            DirectorySecurity security = rootInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleSpecific(rule);
            }

            foreach (SecurityIdentifier sid in allowed)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
            {
                security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            }

            _persist(rootInfo, security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            failures.Add($"{root}: cannot set its DACL ({ex.GetType().Name}: {ex.Message})");
        }

        foreach (FileSystemInfo entry in Descendants(rootInfo, failures))
        {
            try
            {
                FileSystemSecurity security = entry is DirectoryInfo dir ? dir.GetAccessControl() : ((FileInfo)entry).GetAccessControl();
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                {
                    security.RemoveAccessRuleSpecific(rule);
                }

                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
                {
                    security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                }

                _persist(entry, security);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                failures.Add($"{entry.FullName}: cannot reset its DACL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // The result is what the file system says now, not what was attempted.
        List<string> violations = VerifyDirectoryTree(root, additionalFullControl).ToList();
        if (violations.Count == 0)
        {
            _logger.LogInformation("DACL closed on {Root}: SYSTEM, Administrators{Extra}",
                root, additionalFullControl.Count > 0 ? " and the development account" : string.Empty);
        }

        return failures.Concat(violations).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Reports every entry of the tree that grants access to an account other than SYSTEM,
    /// Administrators and <paramref name="additionalFullControl"/>, has another owner, is a reparse
    /// point, or (for the root) still inherits from its parent. Changes nothing.
    /// </summary>
    public IReadOnlyList<string> VerifyDirectoryTree(string root, IReadOnlyCollection<SecurityIdentifier> additionalFullControl)
    {
        HashSet<SecurityIdentifier> allowed = AllowedAccounts(additionalFullControl);
        var violations = new List<string>();
        var rootInfo = new DirectoryInfo(root);

        if (!rootInfo.Exists)
        {
            return [$"{root}: does not exist"];
        }

        if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return [$"{root}: is a reparse point (junction or symbolic link)"];
        }

        if (!rootInfo.GetAccessControl().AreAccessRulesProtected)
        {
            violations.Add($"{root}: still inherits permissions from its parent");
        }

        foreach (FileSystemInfo entry in Descendants(rootInfo, violations).Prepend(rootInfo))
        {
            try
            {
                FileSystemSecurity security = entry is DirectoryInfo dir ? dir.GetAccessControl() : ((FileInfo)entry).GetAccessControl();
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    if (rule.AccessControlType == AccessControlType.Allow
                        && rule.IdentityReference is SecurityIdentifier sid
                        && !allowed.Contains(sid))
                    {
                        violations.Add($"{entry.FullName}: grants {Describe(sid)} {rule.FileSystemRights}");
                    }
                }

                if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !allowed.Contains(owner))
                {
                    violations.Add($"{entry.FullName}: owned by {Describe(owner)}");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                violations.Add($"{entry.FullName}: cannot read its DACL ({ex.GetType().Name})");
            }
        }

        return violations;
    }

    private static HashSet<SecurityIdentifier> AllowedAccounts(IReadOnlyCollection<SecurityIdentifier> additional) =>
    [
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        .. additional,
    ];

    /// <summary>Every file and directory under <paramref name="root"/>, never through a reparse point.</summary>
    private static IEnumerable<FileSystemInfo> Descendants(DirectoryInfo root, List<string> problems)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            DirectoryInfo current = pending.Pop();
            FileSystemInfo[] children;
            try
            {
                children = current.GetFileSystemInfos("*", new EnumerationOptions
                {
                    AttributesToSkip = 0,
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = false,
                });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                problems.Add($"{current.FullName}: cannot list its content ({ex.GetType().Name})");
                continue;
            }

            foreach (FileSystemInfo child in children)
            {
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    problems.Add($"{child.FullName}: is a reparse point (junction or symbolic link); not followed");
                    continue;
                }

                yield return child;
                if (child is DirectoryInfo directory)
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static string Describe(SecurityIdentifier sid)
    {
        try
        {
            return $"{sid.Translate(typeof(NTAccount))} ({sid.Value})";
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }

    private static void PersistAcl(FileSystemInfo entry, FileSystemSecurity security)
    {
        switch (entry)
        {
            case DirectoryInfo directory:
                directory.SetAccessControl((DirectorySecurity)security);
                break;
            case FileInfo file:
                file.SetAccessControl((FileSecurity)security);
                break;
        }
    }
}
