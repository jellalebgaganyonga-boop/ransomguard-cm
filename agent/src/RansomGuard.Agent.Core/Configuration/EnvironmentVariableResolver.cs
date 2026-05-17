namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// Resolves environment variables (e.g., %USERPROFILE%, %ProgramData%) in configuration paths.
/// </summary>
public static class EnvironmentVariableResolver
{
    /// <summary>
    /// Expands environment variables in the given path string.
    /// </summary>
    /// <param name="path">Path containing environment variable placeholders.</param>
    /// <returns>Path with environment variables expanded.</returns>
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// Expands environment variables in all paths in the array.
    /// </summary>
    /// <param name="paths">Array of paths containing environment variable placeholders.</param>
    /// <returns>Array of paths with environment variables expanded.</returns>
    public static string[] ResolvePaths(string[] paths)
    {
        if (paths is null || paths.Length == 0)
        {
            return paths ?? [];
        }

        return Array.ConvertAll(paths, ResolvePath);
    }
}
