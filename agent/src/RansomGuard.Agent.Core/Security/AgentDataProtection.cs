using System.Runtime.Versioning;
using System.Security.Principal;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Core.Security;

/// <summary>Who, besides SYSTEM and Administrators, may access the agent's data directories.</summary>
/// <param name="AdditionalFullControl">Empty outside Development.</param>
[SupportedOSPlatform("windows")]
public sealed record DataProtectionPolicy(IReadOnlyList<SecurityIdentifier> AdditionalFullControl)
{
    /// <summary>
    /// Production (and any environment that is not Development): SYSTEM and Administrators only.
    /// Development: the account running the agent is added, or a developer could no longer run it.
    /// </summary>
    public static DataProtectionPolicy For(string? environmentName, SecurityIdentifier currentAccount) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            ? new DataProtectionPolicy([currentAccount])
            : new DataProtectionPolicy([]);

    /// <summary>True when a non-administrative account is granted access (Development only).</summary>
    public bool AddsDevelopmentAccount => AdditionalFullControl.Count > 0;
}

/// <summary>The outcome of closing the agent's data directories.</summary>
/// <param name="Roots">The directories closed (each with its whole tree).</param>
/// <param name="Errors">Empty when every tree is closed; otherwise the agent must not start.</param>
public sealed record DataProtectionResult(IReadOnlyList<string> Roots, IReadOnlyList<string> Errors);

/// <summary>
/// AGT-KEY-001: the agent's data -- encrypted database, database and audit-signing keys, logs,
/// updates -- lives under <c>%ProgramData%\RansomGuard-CM</c>, which inherited
/// <c>BUILTIN\Users</c> read from <c>C:\ProgramData</c>. With DPAPI LocalMachine keys, any local
/// account could read and decrypt them, ransomware running as a user included. A readable database
/// next to protected keys protects nothing: the whole root is closed, not only <c>keys\</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentDataProtection
{
    /// <summary>The agent's data root.</summary>
    public const string DefaultDataRoot = @"%ProgramData%\RansomGuard-CM";

    /// <summary>
    /// The trees to close: the data root, plus each configured data directory (database, keys,
    /// logs) that lies outside it. A configured directory that is a drive root or a shared system
    /// folder is refused, never re-permissioned: closing <c>C:\</c> or <c>C:\Users</c> to
    /// Administrators would break the machine.
    /// </summary>
    public static (IReadOnlyList<string> Roots, IReadOnlyList<string> Errors) ResolveRoots(AgentConfiguration config) =>
        ResolveRoots(config, DefaultDataRoot);

    /// <summary>Tests: the same, with another data root than %ProgramData%.</summary>
    internal static (IReadOnlyList<string> Roots, IReadOnlyList<string> Errors) ResolveRoots(AgentConfiguration config, string dataRootSetting)
    {
        string dataRoot = Normalize(EnvironmentVariableResolver.ResolvePath(dataRootSetting));
        var roots = new List<string> { dataRoot };
        var errors = new List<string>();

        string? database = ConfigurationValueRules.DataSource(EnvironmentVariableResolver.ResolvePath(config.Database.ConnectionString));
        (string Setting, string? Directory)[] configured =
        [
            ("Agent:Database:ConnectionString", database is null ? null : Path.GetDirectoryName(Path.GetFullPath(database))),
            ("Agent:Database:KeyDirectory", Path.GetFullPath(EnvironmentVariableResolver.ResolvePath(config.Database.KeyDirectory))),
            ("Agent:Logging:LogFilePath", Path.GetDirectoryName(Path.GetFullPath(EnvironmentVariableResolver.ResolvePath(config.Logging.LogFilePath)))),
        ];

        foreach ((string setting, string? directory) in configured)
        {
            if (directory is null)
            {
                continue;
            }

            string normalized = Normalize(directory);
            if (roots.Any(root => IsSameOrUnder(normalized, root)))
            {
                continue;
            }

            if (IsSharedLocation(normalized))
            {
                errors.Add($"{setting}: {directory} is a shared or system folder; the agent closes its data "
                    + "directories to SYSTEM and Administrators and will not do it there. Give the agent a "
                    + $"directory of its own (default: {DefaultDataRoot}).");
                continue;
            }

            roots.RemoveAll(root => IsSameOrUnder(root, normalized));
            roots.Add(normalized);
        }

        return (roots, errors);
    }

    /// <summary>Creates each root if needed, closes its tree, and verifies it.</summary>
    public static DataProtectionResult Protect(
        AgentConfiguration config, DataProtectionPolicy policy, DaclEnforcer enforcer) =>
        Protect(config, policy, enforcer, DefaultDataRoot);

    /// <summary>Tests: the same, with another data root than %ProgramData%.</summary>
    internal static DataProtectionResult Protect(
        AgentConfiguration config, DataProtectionPolicy policy, DaclEnforcer enforcer, string dataRootSetting)
    {
        (IReadOnlyList<string> roots, IReadOnlyList<string> errors) = ResolveRoots(config, dataRootSetting);
        if (errors.Count > 0)
        {
            return new DataProtectionResult(roots, errors);
        }

        var problems = new List<string>();
        foreach (string root in roots)
        {
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                problems.Add($"{root}: cannot be created ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            problems.AddRange(enforcer.SecureDirectoryTree(root, policy.AdditionalFullControl));
        }

        return new DataProtectionResult(roots, problems);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSameOrUnder(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedLocation(string directory)
    {
        if (string.Equals(directory, Normalize(Path.GetPathRoot(directory) ?? directory), StringComparison.OrdinalIgnoreCase))
        {
            return true; // a drive root
        }

        string windows = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        // The profiles root (C:\Users), from %PUBLIC%: under LocalSystem the current profile is
        // C:\Windows\System32\config\systemprofile and says nothing about it.
        string? publicProfile = Environment.GetEnvironmentVariable("PUBLIC");
        string? profiles = string.IsNullOrEmpty(publicProfile) ? null : Path.GetDirectoryName(Normalize(publicProfile));

        string?[] shared =
        [
            windows,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            profiles,
        ];

        // The folder itself, or one of its ancestors: closing it would close the shared folder below.
        if (shared.Where(s => !string.IsNullOrEmpty(s)).Select(s => Normalize(s!)).Any(s => IsSameOrUnder(s, directory)))
        {
            return true;
        }

        // Inside Windows or a user profile: not a directory of the agent's own.
        return IsSameOrUnder(directory, windows) || (profiles is not null && IsSameOrUnder(directory, Normalize(profiles)));
    }
}
