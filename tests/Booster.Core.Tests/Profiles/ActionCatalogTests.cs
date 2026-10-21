using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.FakePowerPlanProvider;

namespace Booster.Core.Tests.Profiles;

public class ActionCatalogTests
{
    private readonly FakeProcessProvider _processes = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeAppCloser _closer = new();

    private ActionCatalog Catalog(FakePowerPlanProvider? power = null) =>
        new(power ?? new FakePowerPlanProvider(), _processes, _processes, _closer, new FakeTimeProvider());

    private static GamingProfile With(ProfileActionEntry entry) => ProfileFactory.Profile(actions: ProfileFactory.ActionsWith(entry));

    [Fact]
    public async Task Power_plan_targets_come_from_the_profile_in_order()
    {
        var power = new FakePowerPlanProvider(Balanced, PowerSaver, HighPerformance);
        var profile = With(new ProfileActionEntry(ActionIds.PowerPlan, ActionMode.AskFirst,
            TargetSchemes: ["e9a42b02-d5df-448d-aa00-03f14749eb61", PowerSaver.Id.ToString(), ProfileFactory.HighPerformance]));

        var action = Catalog(power).Create(ActionIds.PowerPlan, profile, null);

        Assert.IsType<PowerPlanAction>(action);
        Assert.Equal("«Сбалансированная» → «Экономия энергии»", (await action.CheckSupportAsync(Ct)).Summary);
    }

    [Theory]
    [InlineData("High", "High")]
    [InlineData("AboveNormal", "AboveNormal")]
    [InlineData(null, "AboveNormal")]
    public async Task Game_priority_target_comes_from_the_profile_value(string? value, string expected)
    {
        var game = new GameProcess("cs2", _processes.Start("cs2", 1234));
        var profile = With(new ProfileActionEntry(ActionIds.GamePriority, ActionMode.Optional, Value: value));

        var action = Catalog().Create(ActionIds.GamePriority, profile, game);

        Assert.IsType<GamePriorityAction>(action);
        Assert.Equal(expected, (await action.CaptureAsync(Ct)).AppliedValue!.Value.GetProperty("priority").GetString());
    }

    [Theory]
    [InlineData("RealTime")]
    [InlineData("Idle")]
    [InlineData("Normal")]
    public void Game_priority_value_outside_the_allowed_pair_is_not_created(string value)
    {
        var profile = With(new ProfileActionEntry(ActionIds.GamePriority, ActionMode.Optional, Value: value));

        Assert.Null(Catalog().Create(ActionIds.GamePriority, profile, null));
    }

    [Fact]
    public async Task Background_apps_come_from_the_profile_selection()
    {
        var game = new GameProcess("cs2", _processes.Start("cs2", 1234));
        _processes.Start("chrome", 2000);
        var profile = ProfileFactory.Profile(games: ["cs2"], userSelected: ["chrome", "Discord"]);

        var action = Catalog().Create(ActionIds.BackgroundApps, profile, game);

        Assert.IsType<BackgroundAppsAction>(action);
        Assert.Equal("Закрыть: chrome", (await action.CheckSupportAsync(Ct)).Summary);
    }

    [Theory]
    [InlineData("registry-tweaks")]
    [InlineData("unknown")]
    public void Not_implemented_action_is_null(string actionId)
    {
        Assert.Null(Catalog().Create(actionId, ProfileFactory.Profile(), null));
    }
}
