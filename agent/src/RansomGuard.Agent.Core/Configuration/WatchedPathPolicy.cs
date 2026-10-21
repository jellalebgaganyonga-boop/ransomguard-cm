namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// AGT-PATH-001: a watched folder that resolves into a system or service profile is refused --
/// never watched, never given canaries, never created.
/// </summary>
/// <remarks>
/// The service runs as LocalSystem, where %USERPROFILE% is
/// %WINDIR%\System32\config\systemprofile. The shipped paths (%USERPROFILE%\Desktop, ...) used to
/// resolve there: the agent created the folders itself, planted its canaries in them and reported
/// SENTINEL and ENTROPY active while watching a profile nobody uses. A refused folder counts as
/// missing, so a module left with none reads inactive / no_watch_directory.
/// </remarks>
public static class WatchedPathPolicy
{
    /// <summary>The profiles of the system accounts: LocalSystem (64- and 32-bit views), LocalService, NetworkService.</summary>
    public static IReadOnlyList<string> RefusedRoots { get; } = RootsUnder(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// <summary>True when <paramref name="path"/> is a system or service profile, or lies inside one.</summary>
    public static bool IsRefused(string path)
    {
        string full = Normalize(path);
        return RefusedRoots.Any(root =>
            full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> RootsUnder(string windows) =>
    [
        Normalize(Path.Combine(windows, "System32", "config", "systemprofile")),
        Normalize(Path.Combine(windows, "SysWOW64", "config", "systemprofile")),
        Normalize(Path.Combine(windows, "ServiceProfiles")),
    ];

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
