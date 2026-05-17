using FluentValidation;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Service;

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration section to strongly-typed options
builder.Services.Configure<AgentConfiguration>(
    builder.Configuration.GetSection(AgentConfiguration.SectionName));

// Register FluentValidation validator
builder.Services.AddSingleton<IValidator<AgentConfiguration>, AgentConfigurationValidator>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Fail-fast: validate configuration at startup
ValidateConfiguration(host.Services);

host.Run();

/// <summary>
/// Validates the agent configuration at startup. Throws if invalid (fail-fast principle).
/// </summary>
static void ValidateConfiguration(IServiceProvider services)
{
    var configuration = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgentConfiguration>>();
    var validator = services.GetRequiredService<IValidator<AgentConfiguration>>();

    AgentConfiguration config = configuration.CurrentValue;
    FluentValidation.Results.ValidationResult result = validator.Validate(config);

    if (!result.IsValid)
    {
        var errors = string.Join(Environment.NewLine, result.Errors.Select(e => $"  - {e.PropertyName}: {e.ErrorMessage}"));
        throw new InvalidOperationException(
            $"Agent configuration is invalid. The service cannot start.{Environment.NewLine}{errors}");
    }
}
