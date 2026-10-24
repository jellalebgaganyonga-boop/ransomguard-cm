using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Diagnostics;

/// <summary>
/// Stops a detection module on demand, through its real failure path — for the LOT 2 demo and
/// for tests. Development only: anywhere else the key that turns it on refuses the startup,
/// and the injector registered is <see cref="NoModuleFaultInjector"/>.
/// </summary>
/// <remarks>
/// Each detector observes <see cref="WhenStopRequestedAsync"/> with its other tasks
/// (<see cref="ModuleTaskObserver"/>). When the operator creates
/// <c>%ProgramData%\RansomGuard-CM\dev\fault\&lt;MODULE&gt;.stop</c>, the task faults with
/// <see cref="ModuleFaultInjectedException"/>: the module goes down exactly as on a real failure
/// (inactive / stopped_unexpectedly, Critical log, ModuleStopped audit entry), and the audit
/// entry says fault_injection: true — a provoked stop never passes for a real one.
/// </remarks>
public interface IModuleFaultInjector
{
    /// <summary>Faults when a stop of <paramref name="moduleCode"/> is requested; never completes otherwise.</summary>
    Task WhenStopRequestedAsync(string moduleCode, CancellationToken cancellationToken);
}

/// <summary>Production: never stops anything.</summary>
public sealed class NoModuleFaultInjector : IModuleFaultInjector
{
    /// <inheritdoc />
    public Task WhenStopRequestedAsync(string moduleCode, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}

/// <summary>A module stopped on purpose by <see cref="DevelopmentModuleFaultInjector"/>.</summary>
public sealed class ModuleFaultInjectedException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ModuleFaultInjectedException()
        : base("Module stopped by Development fault injection.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public ModuleFaultInjectedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ModuleFaultInjectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Development: watches the trigger folder. The module is read from the file name, against the
/// closed list of detectors; the file is consumed (deleted) when read, so a restart does not stop
/// the module again. An unknown name is ignored with a Warning, and consumed too.
/// </summary>
public sealed class DevelopmentModuleFaultInjector : IModuleFaultInjector
{
    /// <summary>The detectors that can be stopped: the four that observe the injector.</summary>
    public static readonly IReadOnlySet<string> StoppableModules = new HashSet<string>(StringComparer.Ordinal)
    {
        ModuleCode.Sentinel, ModuleCode.Entropy, ModuleCode.UsbGuard, ModuleCode.ExfilWatch,
    };

    /// <summary>Default trigger folder.</summary>
    public static readonly string DefaultTriggerDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RansomGuard-CM", "dev", "fault");

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly string _directory;
    private readonly ILogger<DevelopmentModuleFaultInjector> _logger;
    private readonly object _scanGate = new();

    /// <summary>Creates the injector.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="triggerDirectory">The folder watched; null uses <see cref="DefaultTriggerDirectory"/>.</param>
    public DevelopmentModuleFaultInjector(ILogger<DevelopmentModuleFaultInjector> logger, string? triggerDirectory = null)
    {
        _logger = logger;
        _directory = triggerDirectory ?? DefaultTriggerDirectory;
    }

    /// <inheritdoc />
    public async Task WhenStopRequestedAsync(string moduleCode, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            string? consumed = ConsumeTriggerFor(moduleCode);
            if (consumed is not null)
            {
                throw new ModuleFaultInjectedException(
                    $"{moduleCode} stopped by Development fault injection ({Path.GetFileName(consumed)}).");
            }
        }
    }

    /// <summary>
    /// One scan of the folder, serialised between the modules: consumes this module's trigger if
    /// present (returns its path), and consumes unknown names with a Warning.
    /// </summary>
    private string? ConsumeTriggerFor(string moduleCode)
    {
        lock (_scanGate)
        {
            if (!Directory.Exists(_directory))
            {
                return null;
            }

            string? mine = null;
            foreach (string file in Directory.EnumerateFiles(_directory, "*.stop"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!StoppableModules.Contains(name))
                {
                    _logger.LogWarning(
                        "Fault injection: '{File}' names no stoppable module (allowed: {Allowed}); ignored and removed",
                        Path.GetFileName(file), string.Join(", ", StoppableModules));
                    TryDelete(file);
                }
                else if (name == moduleCode && mine is null && TryDelete(file))
                {
                    mine = file;
                }
            }

            return mine;
        }
    }

    private bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Fault injection: could not consume {File}", file);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Fault injection: could not consume {File}", file);
            return false;
        }
    }
}

/// <summary>
/// Where <c>Agent:Development:FaultInjection</c> may appear. Anywhere but Development its mere
/// presence refuses the startup — whatever its value: a shipped configuration must not carry it.
/// </summary>
public static class ModuleFaultInjectionPolicy
{
    /// <summary>The configuration key this policy governs.</summary>
    public const string SettingKey = "Agent:Development:FaultInjection";

    /// <summary>The only environment in which the key may appear.</summary>
    public const string AllowedEnvironment = "Development";

    /// <summary>Warning written at every start while the injector is active.</summary>
    public const string ActiveWarning =
        "Agent:Development:FaultInjection is TRUE: a detection module stops when its trigger file appears in " +
        "%ProgramData%\\RansomGuard-CM\\dev\\fault. Allowed only because the host environment is Development.";

    /// <summary>Null when acceptable, otherwise the refusal message, which names the key and the environment.</summary>
    public static string? Validate(IConfiguration configuration, string? environmentName)
    {
        string? value = configuration[SettingKey];
        if (value is null)
        {
            return null;
        }

        string environment = string.IsNullOrWhiteSpace(environmentName) ? "Production" : environmentName;
        if (!string.Equals(environment, AllowedEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return $"{SettingKey} is refused in environment '{environment}': it can stop a detection module " +
                   "on demand. It may appear only in Development.";
        }

        return bool.TryParse(value, out _)
            ? null
            : $"{SettingKey}: '{value}' is not a valid value. Allowed values: true, false.";
    }

    /// <summary>True only in Development, with the key set to true.</summary>
    public static bool IsEnabled(IConfiguration configuration, string? environmentName) =>
        Validate(configuration, environmentName) is null
        && string.Equals(environmentName, AllowedEnvironment, StringComparison.OrdinalIgnoreCase)
        && bool.TryParse(configuration[SettingKey], out bool enabled) && enabled;
}
