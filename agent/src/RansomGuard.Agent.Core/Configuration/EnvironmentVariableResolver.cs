using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// Expands the paths of the configuration: Windows environment variables, and
/// <c>%EACHUSERPROFILE%</c>, the agent's own token for "every real user of this machine".
/// </summary>
public static class EnvironmentVariableResolver
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Expands the environment variables of one path (%USERPROFILE%, %ProgramData%, …).</summary>
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>Expands the environment variables of every path.</summary>
    public static string[] ResolvePaths(string[] paths)
    {
        if (paths is null || paths.Length == 0)
        {
            return paths ?? [];
        }

        return Array.ConvertAll(paths, ResolvePath);
    }

    /// <summary>
    /// Expands every path; a path starting with <see cref="UserProfileEnumerator.Token"/> becomes one
    /// path per real user profile (AGT-PATH-001). Its first segment, Desktop or Documents, follows
    /// the user's shell folders when the user's hive is loaded; otherwise the default folder under
    /// the profile is assumed, and said so with a Warning per user.
    /// </summary>
    /// <param name="paths">The configured paths.</param>
    /// <param name="profiles">The real user profiles of this machine.</param>
    /// <param name="logger">Where the assumptions and the refusals are written.</param>
    public static string[] ResolvePaths(string[] paths, UserProfileEnumerator profiles, ILogger logger)
    {
        if (paths is null || paths.Length == 0)
        {
            return paths ?? [];
        }

        IReadOnlyList<UserProfile>? users = null;
        var resolved = new List<string>(paths.Length);
        foreach (string path in paths)
        {
            int at = path.IndexOf(UserProfileEnumerator.Token, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                resolved.Add(ResolvePath(path));
                continue;
            }

            if (at > 0)
            {
                logger.LogError("{Path}: {Token} must begin the path; not watched (AGT-PATH-001)", path, UserProfileEnumerator.Token);
                continue;
            }

            users ??= profiles.Enumerate();
            if (users.Count == 0)
            {
                logger.LogError("{Path}: no user profile on this machine, nothing to watch (AGT-PATH-001)", path);
                continue;
            }

            string[] segments = path[UserProfileEnumerator.Token.Length..]
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (UserProfile user in users)
            {
                resolved.Add(ForUser(user, segments, logger));
            }
        }

        return resolved.ToArray();
    }

    private static string ForUser(UserProfile user, string[] segments, ILogger logger)
    {
        if (segments.Length == 0)
        {
            return user.Root;
        }

        string first = segments[0];
        string? known = null;
        string? shellFolder = null;
        if (first.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            (known, shellFolder) = (user.Desktop, "Desktop");
        }
        else if (first.Equals("Documents", StringComparison.OrdinalIgnoreCase))
        {
            (known, shellFolder) = (user.Documents, "Documents");
        }

        string head;
        if (known is not null)
        {
            head = known;
        }
        else
        {
            head = Path.Combine(user.Root, first);
            if (shellFolder is not null)
            {
                // No silent default: the folder may be redirected (OneDrive, GPO) and we would not know.
                logger.LogWarning(
                    "{Sid}: {Folder} assumed at {Path} -- the user's hive is not loaded (no open session) or the folder is not declared; a redirection (OneDrive, GPO) would not be followed (AGT-PATH-001)",
                    user.Sid, shellFolder, head);
            }
        }

        return segments.Length == 1 ? head : Path.Combine([head, .. segments[1..]]);
    }
}
