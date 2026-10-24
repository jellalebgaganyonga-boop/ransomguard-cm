using RansomGuard.Agent.Core.Configuration;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>AGT-PATH-001: which folders are a system or service profile, and so never watched.</summary>
public sealed class WatchedPathPolicyTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Theory]
    [InlineData(@"System32\config\systemprofile")]
    [InlineData(@"System32\config\systemprofile\Desktop")]
    [InlineData(@"System32\config\systemprofile\Desktop\RansomGuard-TestZone")]
    [InlineData(@"SysWOW64\config\systemprofile\Documents")]
    [InlineData(@"ServiceProfiles\LocalService\Desktop")]
    [InlineData(@"ServiceProfiles\NetworkService\Documents")]
    public void A_system_or_service_profile_is_refused(string underWindows)
    {
        WatchedPathPolicy.IsRefused(Path.Combine(Windows, underWindows)).ShouldBeTrue();
    }

    [Fact]
    public void What_LocalSystem_makes_of_the_shipped_path_is_refused()
    {
        // %USERPROFILE%\Desktop as the service resolves it.
        string asLocalSystem = Path.Combine(Windows, "System32", "config", "systemprofile", "Desktop");

        WatchedPathPolicy.IsRefused(asLocalSystem.ToUpperInvariant()).ShouldBeTrue();
        WatchedPathPolicy.IsRefused(asLocalSystem + Path.DirectorySeparatorChar).ShouldBeTrue();
        WatchedPathPolicy.IsRefused(Path.Combine(@"C:\Users\..", Path.GetRelativePath(@"C:\", asLocalSystem))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"C:\Users\alice\Desktop")]
    [InlineData(@"C:\Users\alice\Documents\RansomGuard-TestZone")]
    [InlineData(@"D:\Partage\Radiologie")]
    public void A_user_or_shared_folder_is_accepted(string path)
    {
        WatchedPathPolicy.IsRefused(path).ShouldBeFalse();
    }

    [Fact]
    public void A_neighbour_whose_name_only_starts_like_a_profile_is_accepted()
    {
        WatchedPathPolicy.IsRefused(Path.Combine(Windows, "System32", "config", "systemprofile2")).ShouldBeFalse();
        WatchedPathPolicy.IsRefused(Path.Combine(Windows, "ServiceProfilesBackup")).ShouldBeFalse();
    }
}
