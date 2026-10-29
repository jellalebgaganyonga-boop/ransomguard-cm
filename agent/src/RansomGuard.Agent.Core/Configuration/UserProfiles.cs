using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>A real person's profile on this machine (AGT-PATH-001).</summary>
/// <param name="Sid">The account SID (S-1-5-21-…).</param>
/// <param name="Root">The profile folder (ProfileImagePath), e.g. C:\Users\alice.</param>
/// <param name="HiveLoaded">True when the user's registry hive is loaded (a session is open), so the shell folders are known.</param>
/// <param name="Desktop">The real Desktop folder (User Shell Folders, redirections followed); null when unknown.</param>
/// <param name="Documents">The real Documents folder (User Shell Folders "Personal"); null when unknown.</param>
public sealed record UserProfile(string Sid, string Root, bool HiveLoaded, string? Desktop, string? Documents);

/// <summary>What the enumerator reads from the registry; replaced in tests.</summary>
public interface IProfileRegistry
{
    /// <summary>Every entry of HKLM\…\ProfileList: the SID and its ProfileImagePath, expanded.</summary>
    IEnumerable<(string Sid, string ProfileImagePath)> ProfileList();

    /// <summary>
    /// The raw "User Shell Folders" value of that user (Desktop, Personal), environment variables
    /// NOT expanded (they are the user's, not the agent's); null when the hive is not loaded or the
    /// value is absent.
    /// </summary>
    string? UserShellFolder(string sid, string valueName);
}

/// <summary>The Windows registry.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProfileRegistry : IProfileRegistry
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    /// <inheritdoc />
    public IEnumerable<(string Sid, string ProfileImagePath)> ProfileList()
    {
        using RegistryKey? list = Registry.LocalMachine.OpenSubKey(ProfileListKey);
        if (list is null)
        {
            yield break;
        }

        foreach (string sid in list.GetSubKeyNames())
        {
            using RegistryKey? profile = list.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is string path)
            {
                yield return (sid, path);
            }
        }
    }

    /// <inheritdoc />
    public string? UserShellFolder(string sid, string valueName)
    {
        using RegistryKey? folders = Registry.Users.OpenSubKey($@"{sid}\{UserShellFoldersKey}");
        return folders?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
}

/// <summary>
/// The real user profiles of this machine, for <c>%EACHUSERPROFILE%</c> (AGT-PATH-001): the
/// service runs as LocalSystem, where %USERPROFILE% is the system profile; the folders to protect
/// are those of the people who use the machine.
/// </summary>
/// <remarks>
/// Read once per resolution, at module start: a profile created afterwards is seen at the next
/// agent restart (AGT-PATH-002).
/// </remarks>
public sealed class UserProfileEnumerator
{
    /// <summary>The configuration token: "every real user profile", one path per profile.</summary>
    public const string Token = "%EACHUSERPROFILE%";

    // Local and domain accounts. Everything else is a system or service account: S-1-5-18/19/20
    // (LocalSystem, LocalService, NetworkService), S-1-5-80-* (service SIDs), S-1-5-82-* (IIS
    // application pools), S-1-5-90-*/S-1-5-96-* (window manager, font driver host).
    private static readonly Regex UserSid = new(@"^S-1-5-21-\d+-\d+-\d+-\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IProfileRegistry _registry;
    private readonly Func<string, bool> _directoryExists;

    /// <summary>The enumerator reading the Windows registry.</summary>
    [SupportedOSPlatform("windows")]
    public static UserProfileEnumerator Windows { get; } = new(new WindowsProfileRegistry());

    /// <summary>Creates an enumerator.</summary>
    /// <param name="registry">Where the profiles are read.</param>
    /// <param name="directoryExists">Whether a profile folder exists; the file system by default.</param>
    public UserProfileEnumerator(IProfileRegistry registry, Func<string, bool>? directoryExists = null)
    {
        _registry = registry;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    /// <summary>The real user profiles whose folder exists; system and service accounts are never returned.</summary>
    public IReadOnlyList<UserProfile> Enumerate()
    {
        var profiles = new List<UserProfile>();
        foreach ((string sid, string imagePath) in _registry.ProfileList())
        {
            if (!UserSid.IsMatch(sid) || string.IsNullOrWhiteSpace(imagePath))
            {
                continue;
            }

            string root = Path.GetFullPath(imagePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Second barrier: a user whose ProfileImagePath points into a system profile is not one.
            if (WatchedPathPolicy.IsRefused(root) || !_directoryExists(root))
            {
                continue;
            }

            string? desktop = _registry.UserShellFolder(sid, "Desktop");
            string? documents = _registry.UserShellFolder(sid, "Personal");
            profiles.Add(new UserProfile(
                sid,
                root,
                HiveLoaded: desktop is not null || documents is not null,
                Expand(desktop, root),
                Expand(documents, root)));
        }

        return profiles;
    }

    // The user's own %USERPROFILE% first, then the machine variables (%SystemDrive%, …).
    private static string? Expand(string? shellFolder, string root)
    {
        if (shellFolder is null)
        {
            return null;
        }

        string withProfile = Regex.Replace(shellFolder, "%USERPROFILE%", root.Replace("$", "$$"), RegexOptions.IgnoreCase);
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(withProfile))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
