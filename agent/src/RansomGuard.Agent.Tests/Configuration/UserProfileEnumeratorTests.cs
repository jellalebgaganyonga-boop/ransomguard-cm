using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Configuration;

/// <summary>
/// AGT-PATH-001: %EACHUSERPROFILE% names the real users of the machine, never a system or
/// service account, and follows their shell folders when they are known.
/// </summary>
public sealed class UserProfileEnumeratorTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private const string Alice = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Bob = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    private readonly FakeRegistry _registry = new();
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);

    private UserProfileEnumerator Enumerator() => new(_registry, _existing.Contains);

    [Fact]
    public void System_and_service_accounts_are_never_a_user_profile()
    {
        _registry.Profiles["S-1-5-18"] = Path.Combine(Windows, "system32", "config", "systemprofile");
        _registry.Profiles["S-1-5-19"] = Path.Combine(Windows, "ServiceProfiles", "LocalService");
        _registry.Profiles["S-1-5-20"] = Path.Combine(Windows, "ServiceProfiles", "NetworkService");
        _registry.Profiles["S-1-5-80-3139157870-2983391045-3678747466-658725712-1809340420"] = @"C:\Users\svc-sql";
        _registry.Profiles["S-1-5-82-271721585-897601226-2024613209-625570482-296978595"] = @"C:\Users\DefaultAppPool";
        _registry.Profiles["S-1-5-90-0-1"] = @"C:\Users\DWM-1";
        _registry.Profiles[Alice] = @"C:\Users\alice";
        foreach (string path in _registry.Profiles.Values) _existing.Add(path);

        IReadOnlyList<UserProfile> users = Enumerator().Enumerate();

        users.Select(u => u.Sid).ShouldBe([Alice]);
        users[0].Root.ShouldBe(@"C:\Users\alice");
    }

    [Fact]
    public void A_user_whose_folder_is_gone_or_points_into_a_system_profile_is_left_out()
    {
        _registry.Profiles[Alice] = @"C:\Users\alice-deleted"; // folder removed, registry entry left behind
        _registry.Profiles[Bob] = Path.Combine(Windows, "System32", "config", "systemprofile"); // tampered entry
        _existing.Add(_registry.Profiles[Bob]);

        Enumerator().Enumerate().ShouldBeEmpty();
    }

    [Fact]
    public void A_loaded_hive_gives_the_real_Desktop_and_Documents_redirections_followed()
    {
        _registry.Profiles[Alice] = @"C:\Users\alice";
        _existing.Add(@"C:\Users\alice");
        // OneDrive "Known Folder Move": the values are the user's, with the user's %USERPROFILE%.
        _registry.ShellFolders[(Alice, "Desktop")] = @"%USERPROFILE%\OneDrive - Hopital\Desktop";
        _registry.ShellFolders[(Alice, "Personal")] = @"%USERPROFILE%\OneDrive - Hopital\Documents";

        UserProfile alice = Enumerator().Enumerate().ShouldHaveSingleItem();

        alice.HiveLoaded.ShouldBeTrue();
        alice.Desktop.ShouldBe(@"C:\Users\alice\OneDrive - Hopital\Desktop"); // never the agent's own profile
        alice.Documents.ShouldBe(@"C:\Users\alice\OneDrive - Hopital\Documents");
    }

    [Fact]
    public void A_hive_not_loaded_leaves_the_shell_folders_unknown()
    {
        _registry.Profiles[Alice] = @"C:\Users\alice";
        _existing.Add(@"C:\Users\alice");

        UserProfile alice = Enumerator().Enumerate().ShouldHaveSingleItem();

        alice.HiveLoaded.ShouldBeFalse();
        alice.Desktop.ShouldBeNull();
        alice.Documents.ShouldBeNull();
    }

    [Fact]
    public void The_token_expands_to_one_path_per_user_following_known_folders_and_saying_what_is_assumed()
    {
        _registry.Profiles[Alice] = @"C:\Users\alice";
        _registry.Profiles[Bob] = @"C:\Users\bob";
        _existing.Add(@"C:\Users\alice");
        _existing.Add(@"C:\Users\bob");
        _registry.ShellFolders[(Alice, "Desktop")] = @"%USERPROFILE%\OneDrive\Desktop";
        _registry.ShellFolders[(Alice, "Personal")] = @"D:\Docs\alice";
        var log = new RecordingLogger<UserProfileEnumeratorTests>();

        string[] resolved = EnvironmentVariableResolver.ResolvePaths(
            [@"%EACHUSERPROFILE%\Desktop\RansomGuard-TestZone", @"%EACHUSERPROFILE%\Documents", @"%EACHUSERPROFILE%\Pictures", @"C:\Partage"],
            Enumerator(), log);

        resolved.ShouldBe([
            @"C:\Users\alice\OneDrive\Desktop\RansomGuard-TestZone",
            @"C:\Users\bob\Desktop\RansomGuard-TestZone",
            @"D:\Docs\alice",
            @"C:\Users\bob\Documents",
            @"C:\Users\alice\Pictures",
            @"C:\Users\bob\Pictures",
            @"C:\Partage",
        ]);
        // Bob's hive is not loaded: his two folders are assumed, and said so; Alice's are known.
        log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ShouldAllBe(m => m.Contains(Bob));
        log.Entries.Count(e => e.Level == LogLevel.Warning).ShouldBe(2);
        log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Desktop") && e.Message.Contains(@"C:\Users\bob\Desktop"));
        log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Documents") && e.Message.Contains(@"C:\Users\bob\Documents"));
    }

    [Fact]
    public void No_user_on_the_machine_resolves_the_token_to_nothing_with_an_Error()
    {
        var log = new RecordingLogger<UserProfileEnumeratorTests>();

        string[] resolved = EnvironmentVariableResolver.ResolvePaths([@"%EACHUSERPROFILE%\Desktop"], Enumerator(), log);

        resolved.ShouldBeEmpty();
        log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("no user profile"));
    }

    [Fact]
    public void The_token_anywhere_but_at_the_start_is_refused()
    {
        _registry.Profiles[Alice] = @"C:\Users\alice";
        _existing.Add(@"C:\Users\alice");
        var log = new RecordingLogger<UserProfileEnumeratorTests>();

        string[] resolved = EnvironmentVariableResolver.ResolvePaths([@"C:\x\%EACHUSERPROFILE%\Desktop"], Enumerator(), log);

        resolved.ShouldBeEmpty();
        log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("must begin the path"));
    }

    [Fact]
    public void What_LocalSystem_makes_of_the_old_token_is_still_refused_by_the_policy()
    {
        // %USERPROFILE% is left to Windows: under LocalSystem it is the system profile, which
        // WatchedPathPolicy refuses in every consumer. The two tokens are not confused.
        string asLocalSystem = Path.Combine(Windows, "System32", "config", "systemprofile", "Desktop");

        WatchedPathPolicy.IsRefused(asLocalSystem).ShouldBeTrue();
        EnvironmentVariableResolver.ResolvePaths([@"%USERPROFILE%\Desktop"], Enumerator(), new RecordingLogger<UserProfileEnumeratorTests>())
            .ShouldBe([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop")]);
    }

    private sealed class FakeRegistry : IProfileRegistry
    {
        public Dictionary<string, string> Profiles { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Sid, string Value), string> ShellFolders { get; } = [];

        public IEnumerable<(string Sid, string ProfileImagePath)> ProfileList() => Profiles.Select(p => (p.Key, p.Value));

        public string? UserShellFolder(string sid, string valueName) =>
            ShellFolders.TryGetValue((sid, valueName), out string? value) ? value : null;
    }
}
