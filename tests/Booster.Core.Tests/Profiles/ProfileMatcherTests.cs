using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Profiles;

public class ProfileMatcherTests
{
    private static GamingProfile Profile(string profileId, params string[] games) =>
        ProfileFactory.Profile(games: games, profileId: profileId);

    [Fact]
    public void Game_name_matches_without_case_and_exe_suffix()
    {
        var match = ProfileMatcher.Match([new ProcessInfo(10, "explorer"), new ProcessInfo(42, "cs2")], [Profile("competitive", "CS2.EXE")]);

        Assert.NotNull(match);
        Assert.Equal("competitive", match.Profile.ProfileId);
        Assert.Equal("cs2", match.GameName);
        Assert.Equal([42], match.ProcessIds);
    }

    [Fact]
    public void First_candidate_profile_wins()
    {
        var running = new[] { new ProcessInfo(1, "dota2"), new ProcessInfo(2, "cs2") };

        var match = ProfileMatcher.Match(running, [Profile("first", "cs2"), Profile("second", "dota2")]);

        Assert.Equal("first", match?.Profile.ProfileId);
        Assert.Equal("cs2", match?.GameName);
    }

    [Fact]
    public void First_game_of_the_profile_wins()
    {
        var running = new[] { new ProcessInfo(1, "dota2"), new ProcessInfo(2, "cs2") };

        var match = ProfileMatcher.Match(running, [Profile("competitive", "cs2", "dota2")]);

        Assert.Equal("cs2", match?.GameName);
        Assert.Equal([2], match?.ProcessIds);
    }

    [Fact]
    public void MatchAll_returns_every_running_game_in_profile_order()
    {
        var running = new[] { new ProcessInfo(2, "b"), new ProcessInfo(1, "a") };

        var matches = ProfileMatcher.MatchAll(running, [Profile("A", "a.exe"), Profile("B", "b.exe")]);

        Assert.Equal(["A/a", "B/b"], matches.Select(m => $"{m.Profile.ProfileId}/{m.GameName}"));
        Assert.Equal([1], matches[0].ProcessIds);
        Assert.Equal([2], matches[1].ProcessIds);
    }

    [Fact]
    public void No_running_game_gives_null()
    {
        Assert.Null(ProfileMatcher.Match([new ProcessInfo(1, "explorer")], [Profile("competitive", "cs2")]));
        Assert.Null(ProfileMatcher.Match([], [Profile("competitive", "cs2")]));
        Assert.Null(ProfileMatcher.Match([new ProcessInfo(1, "cs2")], []));
    }

    [Fact]
    public void Process_ids_are_ascending()
    {
        var running = new[] { new ProcessInfo(300, "cs2"), new ProcessInfo(5, "explorer"), new ProcessInfo(100, "CS2"), new ProcessInfo(200, "cs2") };

        var match = ProfileMatcher.Match(running, [Profile("competitive", "cs2")]);

        Assert.Equal([100, 200, 300], match?.ProcessIds);
    }

    [Fact]
    public void Similar_names_do_not_match()
    {
        var running = new[] { new ProcessInfo(1, "cs2launcher"), new ProcessInfo(2, "cs") };

        Assert.Null(ProfileMatcher.Match(running, [Profile("competitive", "cs2")]));
    }
}
