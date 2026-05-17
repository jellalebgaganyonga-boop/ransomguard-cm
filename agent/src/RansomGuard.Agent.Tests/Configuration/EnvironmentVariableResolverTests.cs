using FluentAssertions;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// Tests for <see cref="EnvironmentVariableResolver"/>.
/// </summary>
public sealed class EnvironmentVariableResolverTests
{
    [Fact]
    public void Should_expand_userprofile_variable()
    {
        string input = @"%USERPROFILE%\Desktop\Test";
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop", "Test");

        string result = EnvironmentVariableResolver.ResolvePath(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void Should_return_path_unchanged_when_no_variables()
    {
        string input = @"C:\SomePath\Test";

        string result = EnvironmentVariableResolver.ResolvePath(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Should_handle_null_path()
    {
        string result = EnvironmentVariableResolver.ResolvePath(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Should_handle_empty_path()
    {
        string result = EnvironmentVariableResolver.ResolvePath("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Should_resolve_multiple_paths()
    {
        string[] input = [@"%USERPROFILE%\Test1", @"C:\Test2"];

        string[] result = EnvironmentVariableResolver.ResolvePaths(input);

        result.Should().HaveCount(2);
        result[0].Should().NotContain("%USERPROFILE%");
        result[1].Should().Be(@"C:\Test2");
    }

    [Fact]
    public void Should_handle_empty_paths_array()
    {
        string[] result = EnvironmentVariableResolver.ResolvePaths([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Should_handle_null_paths_array()
    {
        string[] result = EnvironmentVariableResolver.ResolvePaths(null!);

        result.Should().BeEmpty();
    }
}
