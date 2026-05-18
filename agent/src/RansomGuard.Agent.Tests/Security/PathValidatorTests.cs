using RansomGuard.Agent.Core.Security;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// Tests for <see cref="PathValidator"/> covering CWE-22, CWE-23, CWE-73 attack vectors.
/// </summary>
public sealed class PathValidatorTests
{
    [Fact]
    public void Valid_absolute_path_should_pass()
    {
        PathValidationResult result = PathValidator.Validate(@"C:\Users\Test\Documents");
        result.IsValid.ShouldBeTrue();
        result.CanonicalPath.ShouldNotBeNull();
    }

    [Fact]
    public void Path_with_environment_variable_should_pass()
    {
        PathValidationResult result = PathValidator.Validate(@"%USERPROFILE%\Desktop");
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"C:\Users\..\Windows\System32")]
    [InlineData(@"..\..\etc\passwd")]
    [InlineData(@"C:\Test\..\..\Windows")]
    public void Traversal_sequences_should_be_rejected(string path)
    {
        PathValidationResult result = PathValidator.Validate(path);
        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("traversal");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    public void Reserved_windows_names_should_be_rejected(string name)
    {
        PathValidationResult result = PathValidator.Validate($@"C:\Test\{name}");
        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Reserved");
    }

    [Theory]
    [InlineData(@"C:\Test\file<name")]
    [InlineData(@"C:\Test\file>name")]
    [InlineData(@"C:\Test\file|name")]
    [InlineData(@"C:\Test\file""name")]
    public void Dangerous_characters_should_be_rejected(string path)
    {
        PathValidationResult result = PathValidator.Validate(path);
        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("illegal");
    }

    [Fact]
    public void UNC_paths_should_be_rejected()
    {
        PathValidationResult result = PathValidator.Validate(@"\\server\share\file");
        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("UNC");
    }

    [Fact]
    public void Empty_path_should_be_rejected()
    {
        PathValidator.Validate("").IsValid.ShouldBeFalse();
        PathValidator.Validate("   ").IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Normal_hospital_path_should_pass()
    {
        PathValidationResult result = PathValidator.Validate(@"C:\ProgramData\RansomGuard-CM\data");
        result.IsValid.ShouldBeTrue();
    }
}
