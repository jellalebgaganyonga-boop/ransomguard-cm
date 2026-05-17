using FluentValidation;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// Validates <see cref="AgentConfiguration"/> using FluentValidation rules.
/// Applied at startup to enforce fail-fast behavior on invalid configuration.
/// </summary>
public sealed class AgentConfigurationValidator : AbstractValidator<AgentConfiguration>
{
    /// <summary>
    /// Initializes validation rules for the agent configuration.
    /// </summary>
    public AgentConfigurationValidator()
    {
        RuleFor(x => x.Identity).NotNull().SetValidator(new AgentIdentityOptionsValidator());
        RuleFor(x => x.Detection).NotNull().SetValidator(new DetectionOptionsValidator());
        RuleFor(x => x.Logging).NotNull().SetValidator(new LoggingOptionsValidator());
        RuleFor(x => x.Server).NotNull().SetValidator(new ServerOptionsValidator());
        RuleFor(x => x.Database).NotNull().SetValidator(new DatabaseOptionsValidator());
    }
}

/// <summary>
/// Validates <see cref="AgentIdentityOptions"/>.
/// </summary>
public sealed class AgentIdentityOptionsValidator : AbstractValidator<AgentIdentityOptions>
{
    private const string GuidPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string AutoGenerateMarker = "auto-generated-on-first-run";

    /// <summary>
    /// Initializes validation rules for agent identity.
    /// </summary>
    public AgentIdentityOptionsValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .Must(id => id == AutoGenerateMarker || System.Text.RegularExpressions.Regex.IsMatch(id, GuidPattern))
            .WithMessage("Agent.Id must be a valid GUID or 'auto-generated-on-first-run'.");

        RuleFor(x => x.Hostname).NotEmpty();
        RuleFor(x => x.Version).NotEmpty();

        RuleFor(x => x.Environment)
            .NotEmpty()
            .Must(env => env is "Development" or "Staging" or "Production")
            .WithMessage("Environment must be Development, Staging, or Production.");
    }
}

/// <summary>
/// Validates <see cref="DetectionOptions"/>.
/// </summary>
public sealed class DetectionOptionsValidator : AbstractValidator<DetectionOptions>
{
    /// <summary>
    /// Initializes validation rules for detection options.
    /// </summary>
    public DetectionOptionsValidator()
    {
        RuleFor(x => x.WatchPaths)
            .NotEmpty()
            .WithMessage("Detection.WatchPaths must not be empty.");

        RuleForEach(x => x.WatchPaths)
            .Must(BeValidWindowsPath)
            .WithMessage("Each watch path must be a valid Windows path.");
    }

    private static bool BeValidWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string expanded = System.Environment.ExpandEnvironmentVariables(path);

        try
        {
            _ = Path.GetFullPath(expanded);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Validates <see cref="LoggingOptions"/>.
/// </summary>
public sealed class LoggingOptionsValidator : AbstractValidator<LoggingOptions>
{
    private static readonly string[] ValidLevels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>
    /// Initializes validation rules for logging options.
    /// </summary>
    public LoggingOptionsValidator()
    {
        RuleFor(x => x.MinimumLevel)
            .NotEmpty()
            .Must(level => ValidLevels.Contains(level))
            .WithMessage($"MinimumLevel must be one of: {string.Join(", ", ValidLevels)}.");

        RuleFor(x => x.LogFilePath).NotEmpty();
        RuleFor(x => x.MaxFileSizeMB).InclusiveBetween(1, 500);
        RuleFor(x => x.RetainedFileCount).InclusiveBetween(1, 365);
    }
}

/// <summary>
/// Validates <see cref="ServerOptions"/>.
/// </summary>
public sealed class ServerOptionsValidator : AbstractValidator<ServerOptions>
{
    /// <summary>
    /// Initializes validation rules for server options.
    /// </summary>
    public ServerOptionsValidator()
    {
        RuleFor(x => x.BaseUrl)
            .NotEmpty()
            .Must(BeValidHttpsUri)
            .WithMessage("Server.BaseUrl must be a valid HTTPS URI.");

        RuleFor(x => x.HeartbeatIntervalSeconds).InclusiveBetween(10, 3600);
        RuleFor(x => x.ConnectionTimeoutSeconds).InclusiveBetween(1, 120);
    }

    private static bool BeValidHttpsUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}

/// <summary>
/// Validates <see cref="DatabaseOptions"/>.
/// </summary>
public sealed class DatabaseOptionsValidator : AbstractValidator<DatabaseOptions>
{
    /// <summary>
    /// Initializes validation rules for database options.
    /// </summary>
    public DatabaseOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.MaxRetentionDays).InclusiveBetween(1, 3650);
    }
}
