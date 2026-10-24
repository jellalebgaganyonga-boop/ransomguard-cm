using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Service;

/// <summary>
/// The one way the agent builds its host and reads its configuration -- the service and every CLI
/// sub-command alike (AGT-SVC-001).
/// </summary>
/// <remarks>
/// A Windows service is started by the Service Control Manager with C:\Windows\System32 as its
/// working directory. <c>Host.CreateApplicationBuilder(args)</c> roots configuration at the working
/// directory, and <c>AddWindowsService</c> does not change that on a HostApplicationBuilder (only
/// <c>UseWindowsService</c> on the older IHostBuilder does). The installed agent therefore looked
/// for appsettings.json in System32. The content root is the executable's directory, always.
/// </remarks>
public static class AgentHost
{
    /// <summary>The configuration file the agent cannot run without.</summary>
    public const string ConfigurationFileName = "appsettings.json";

    /// <summary>Host builder rooted at the executable's directory, whatever the working directory.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args) =>
        CreateBuilder(args, AppContext.BaseDirectory);

    /// <summary>Host builder rooted at <paramref name="contentRoot"/> (tests use a temporary directory).</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args, string contentRoot) =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = contentRoot,
        });

    /// <summary>
    /// Validates the configuration once. Refuses when appsettings.json is missing from the content
    /// root: the host treats that file as optional, and settings from the environment alone could
    /// otherwise start an agent silently on default values.
    /// </summary>
    public static AgentConfigurationLoadResult LoadConfiguration(HostApplicationBuilder builder)
    {
        string configurationFile = Path.Combine(builder.Environment.ContentRootPath, ConfigurationFileName);
        if (!File.Exists(configurationFile))
        {
            return new AgentConfigurationLoadResult(null,
            [
                $"{ConfigurationFileName}: not found at {configurationFile}. "
                + "The agent reads its configuration from the directory of its executable; "
                + "reinstall the agent or restore the file.",
            ]);
        }

        return AgentConfigurationLoader.Load(builder.Configuration, builder.Environment.EnvironmentName);
    }
}
