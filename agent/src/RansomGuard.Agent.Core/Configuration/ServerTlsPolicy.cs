namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// When <c>Agent:Server:TrustAnyCertificate</c> may be true. It disables TLS validation of the
/// GRID server certificate: anyone on the network path could pose as the GRID, receive alerts
/// and the enrollment token, and send commands. It is therefore allowed only in the
/// Development host environment, against a server on this machine.
/// </summary>
public static class ServerTlsPolicy
{
    /// <summary>The configuration key this policy governs.</summary>
    public const string SettingKey = "Agent:Server:TrustAnyCertificate";

    /// <summary>The only environment in which the setting may be true.</summary>
    public const string AllowedEnvironment = "Development";

    /// <summary>Warning written at every start while the setting is active.</summary>
    public const string ActiveWarning =
        "Agent:Server:TrustAnyCertificate is TRUE: TLS validation of the GRID server certificate is " +
        "disabled. Allowed only because the host environment is Development and the server is local.";

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "::1"];

    /// <summary>
    /// Returns null when the configuration is acceptable, otherwise the refusal message, which
    /// names the key and the environment.
    /// </summary>
    /// <param name="trustAnyCertificate">Value of Agent:Server:TrustAnyCertificate.</param>
    /// <param name="baseUrl">Value of Agent:Server:BaseUrl.</param>
    /// <param name="environmentName">
    /// .NET host environment name. Null or empty counts as Production, as it does in .NET.
    /// </param>
    public static string? Validate(bool trustAnyCertificate, string? baseUrl, string? environmentName)
    {
        if (!trustAnyCertificate)
            return null;

        string environment = string.IsNullOrWhiteSpace(environmentName) ? "Production" : environmentName;
        if (!string.Equals(environment, AllowedEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return $"{SettingKey}=true is refused in environment '{environment}': it disables TLS " +
                   "validation of the GRID server certificate. It is allowed only in Development, " +
                   "against a local server (localhost, 127.0.0.1, ::1).";
        }

        if (!IsLoopback(baseUrl))
        {
            return $"{SettingKey}=true is refused in environment '{environment}' because " +
                   $"Agent:Server:BaseUrl ('{baseUrl}') is not a local server " +
                   "(localhost, 127.0.0.1, ::1).";
        }

        return null;
    }

    private static bool IsLoopback(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.Trim('[', ']');
        return LoopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    }
}
