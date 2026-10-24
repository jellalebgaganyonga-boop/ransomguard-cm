using Microsoft.Data.Sqlite;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// Value rules shared by the configuration validation: enum-valued settings and paths.
/// </summary>
public static class ConfigurationValueRules
{
    /// <summary>
    /// True when <paramref name="value"/> is the name of a defined member of
    /// <typeparamref name="TEnum"/> (case-insensitive). Numeric forms are refused: Enum.TryParse
    /// accepts "5" and returns an undefined value. Comma-separated forms are refused too: they
    /// would OR two members into a value that may happen to be defined.
    /// </summary>
    public static bool IsDefinedEnumName<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.Contains(',') || char.IsDigit(trimmed[0]) || trimmed[0] is '-' or '+')
            return false;

        return Enum.TryParse(trimmed, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed);
    }

    /// <summary>The allowed names of <typeparamref name="TEnum"/>, for refusal messages.</summary>
    public static string AllowedEnumNames<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>());

    /// <summary>
    /// True when <paramref name="path"/> is fully qualified once environment variables are
    /// resolved. A relative path resolves against the working directory, which is System32 for
    /// a Windows service; an unresolved %VARIABLE% is refused too.
    /// </summary>
    public static bool IsAbsoluteAfterExpansion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string expanded = Environment.ExpandEnvironmentVariables(path);
        return !expanded.Contains('%') && Path.IsPathFullyQualified(expanded);
    }

    /// <summary>The Data Source of a SQLite connection string, or null if it cannot be read.</summary>
    public static string? DataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        try
        {
            return new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
