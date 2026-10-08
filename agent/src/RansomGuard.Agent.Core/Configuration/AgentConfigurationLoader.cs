using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using RansomGuard.Agent.Core.Detection.IronClad;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>Outcome of <see cref="AgentConfigurationLoader.Load"/>.</summary>
/// <param name="Configuration">The validated configuration, or null when it was refused.</param>
/// <param name="Errors">Why it was refused, one line per setting, each naming the exact key.</param>
public sealed record AgentConfigurationLoadResult(AgentConfiguration? Configuration, IReadOnlyList<string> Errors);

/// <summary>
/// Binds and validates the agent configuration ONCE, before anything consumes it. Everything
/// downstream reads the validated object, never raw IConfiguration with its own <c>??</c>
/// fallback: a missing value used to become a relative path (System32 for a Windows service),
/// and the validator's refusal was then written to a log file located by that same fallback —
/// the refusal happened and nobody saw it.
/// </summary>
public static class AgentConfigurationLoader
{
    /// <summary>
    /// Binds the <c>Agent</c> section and validates it, together with the TLS policy and the
    /// enum-valued settings that live outside that section.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="environmentName">The .NET host environment name (null counts as Production).</param>
    public static AgentConfigurationLoadResult Load(IConfiguration configuration, string? environmentName)
    {
        var errors = new List<string>();
        AgentConfiguration? config = null;

        try
        {
            config = configuration.GetSection(AgentConfiguration.SectionName).Get<AgentConfiguration>();
        }
        catch (InvalidOperationException ex)
        {
            // The binder refuses a value it cannot convert (e.g. text for a number).
            errors.Add($"{AgentConfiguration.SectionName}: {ex.Message}");
        }

        if (config is null && errors.Count == 0)
        {
            errors.Add($"{AgentConfiguration.SectionName}: the configuration section is missing.");
        }

        if (config is not null)
        {
            ValidationResult result = new AgentConfigurationValidator().Validate(config);
            errors.AddRange(result.Errors.Select(e => $"{ToKey(e.PropertyName)}: {e.ErrorMessage}"));

            string? tlsRefusal = ServerTlsPolicy.Validate(
                config.Server?.TrustAnyCertificate ?? false, config.Server?.BaseUrl, environmentName);
            if (tlsRefusal is not null)
            {
                errors.Add(tlsRefusal);
            }
        }

        // Enum-valued settings bound outside the Agent section are validated here too: the
        // binder would otherwise accept "5" as an undefined value, or throw on a bad name.
        RequireDefinedEnumName<IronCladCommunicationMode>(configuration, "IronClad:CommunicationMode", errors);

        return new AgentConfigurationLoadResult(errors.Count == 0 ? config : null, errors);
    }

    /// <summary>The configuration key of a validated property, e.g. "Server.BaseUrl" -> "Agent:Server:BaseUrl".</summary>
    private static string ToKey(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? AgentConfiguration.SectionName
            : $"{AgentConfiguration.SectionName}:{propertyName.Replace('.', ':')}";

    private static void RequireDefinedEnumName<TEnum>(IConfiguration configuration, string key, List<string> errors)
        where TEnum : struct, Enum
    {
        string? value = configuration[key];
        if (value is not null && !ConfigurationValueRules.IsDefinedEnumName<TEnum>(value))
        {
            errors.Add($"{key}: '{value}' is not a valid value. Allowed values: " +
                       $"{ConfigurationValueRules.AllowedEnumNames<TEnum>()}.");
        }
    }
}
