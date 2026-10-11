using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>Tests that change the process working directory never run beside other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkingDirectoryCollection
{
    public const string Name = "Process working directory";
}

/// <summary>
/// AGT-SVC-001: the agent reads its configuration from the directory of its executable, never
/// from the working directory (System32 for a Windows service), and refuses to start without
/// appsettings.json.
/// </summary>
[Collection(WorkingDirectoryCollection.Name)]
public sealed class AgentHostTests : IDisposable
{
    // No file watcher on the temporary directories, so they can be deleted.
    private static readonly string[] NoReload = ["--hostBuilder:reloadConfigOnChange=false"];

    private readonly string _originalWorkingDirectory = Directory.GetCurrentDirectory();
    private readonly List<string> _directories = [];

    [Fact]
    public void Builder_is_rooted_at_the_executable_directory_whatever_the_working_directory()
    {
        // A working directory like System32: not the executable's directory, with its own file.
        string elsewhere = NewDirectory();
        WriteSettings(elsewhere, "https://from-the-working-directory.example");
        Directory.SetCurrentDirectory(elsewhere);

        HostApplicationBuilder builder = AgentHost.CreateBuilder(NoReload);
        try
        {
            Normalize(builder.Environment.ContentRootPath).ShouldBe(Normalize(AppContext.BaseDirectory));
            builder.Configuration["Agent:Server:BaseUrl"].ShouldNotBe("https://from-the-working-directory.example");
        }
        finally
        {
            builder.Configuration.Dispose();
        }
    }

    [Fact]
    public void Configuration_comes_from_the_content_root_not_the_working_directory()
    {
        string executableDirectory = NewDirectory();
        WriteSettings(executableDirectory, "https://grid.from-content-root.example");
        string elsewhere = NewDirectory();
        WriteSettings(elsewhere, "https://from-the-working-directory.example");
        Directory.SetCurrentDirectory(elsewhere);

        HostApplicationBuilder builder = AgentHost.CreateBuilder(NoReload, executableDirectory);
        try
        {
            AgentConfigurationLoadResult load = AgentHost.LoadConfiguration(builder);

            load.Errors.ShouldBeEmpty();
            load.Configuration.ShouldNotBeNull().Server.BaseUrl.ShouldBe("https://grid.from-content-root.example");
        }
        finally
        {
            builder.Configuration.Dispose();
        }
    }

    [Fact]
    public void Missing_appsettings_is_refused_even_when_the_environment_supplies_every_setting()
    {
        string executableDirectory = NewDirectory();

        HostApplicationBuilder builder = AgentHost.CreateBuilder(NoReload, executableDirectory);
        try
        {
            // Everything the validator requires, as environment variables could supply it.
            builder.Configuration.AddInMemoryCollection(ValidSettings("https://grid.example.org"));

            // Control: these settings alone pass validation -- only the file check refuses.
            AgentConfigurationLoader.Load(builder.Configuration, builder.Environment.EnvironmentName)
                .Configuration.ShouldNotBeNull();

            AgentConfigurationLoadResult load = AgentHost.LoadConfiguration(builder);

            load.Configuration.ShouldBeNull();
            string error = load.Errors.ShouldHaveSingleItem();
            error.ShouldStartWith("appsettings.json: not found at ");
            error.ShouldContain(Path.Combine(executableDirectory, "appsettings.json"));
        }
        finally
        {
            builder.Configuration.Dispose();
        }
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalWorkingDirectory);
        foreach (string directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary directory does not fail the test.
            }
        }
    }

    private string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rg-agenthost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    private static Dictionary<string, string?> ValidSettings(string baseUrl) => new()
    {
        ["Agent:Identity:Id"] = "auto-generated-on-first-run",
        ["Agent:Identity:Hostname"] = "AUTO",
        ["Agent:Identity:Version"] = "0.3.0",
        ["Agent:Detection:WatchPaths:0"] = @"C:\Temp",
        ["Agent:Server:BaseUrl"] = baseUrl,
    };

    private static void WriteSettings(string directory, string baseUrl)
    {
        string json = $$"""
            {
              "Agent": {
                "Identity": { "Id": "auto-generated-on-first-run", "Hostname": "AUTO", "Version": "0.3.0" },
                "Detection": { "WatchPaths": [ "C:\\Temp" ] },
                "Server": { "BaseUrl": "{{baseUrl}}" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(directory, AgentHost.ConfigurationFileName), json);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
