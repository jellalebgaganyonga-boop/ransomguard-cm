using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace RansomGuard.Agent.Core.Security;

/// <summary>
/// Serilog enricher that redacts sensitive data patterns from log messages (CWE-200, CWE-532).
/// Applies to all log sinks to prevent accidental exposure of secrets.
/// </summary>
public sealed partial class LogRedactionEnricher : ILogEventEnricher
{
    private const string RedactedPlaceholder = "[REDACTED]";

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Redact sensitive properties by name
        var keysToRedact = logEvent.Properties
            .Where(p => IsSensitivePropertyName(p.Key))
            .Select(p => p.Key)
            .ToList();

        foreach (string key in keysToRedact)
        {
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty(key, RedactedPlaceholder));
        }
    }

    private static bool IsSensitivePropertyName(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("password") ||
               lower.Contains("secret") ||
               lower.Contains("apikey") ||
               lower.Contains("api_key") ||
               lower.Contains("token") ||
               lower.Contains("connectionstring") ||
               lower.Contains("private_key") ||
               lower.Contains("privatekey");
    }
}
