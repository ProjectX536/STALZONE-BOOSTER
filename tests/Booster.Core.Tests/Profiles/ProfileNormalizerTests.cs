using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using static Booster.Core.Tests.TestSupport.ProfileFactory;

namespace Booster.Core.Tests.Profiles;

public class ProfileNormalizerTests
{
    private static ConfigLoadResult<GamingProfile> Normalize(GamingProfile profile) =>
        ProfileNormalizer.Normalize(profile, "Competitive.json");

    private static void AssertAllInvalidValue(ConfigLoadResult<GamingProfile> result, int count)
    {
        Assert.Equal(count, result.Warnings.Count);
        Assert.All(result.Warnings, w =>
        {
            Assert.Equal(ConfigWarningKind.InvalidValue, w.Kind);
            Assert.Equal("Competitive.json", w.File);
        });
    }

    [Fact]
    public void Valid_profile_is_returned_unchanged()
    {
        var profile = Profile(games: ["cs2"], userSelected: ["chrome"], enabledOptional: [ActionIds.GamePriority]);

        var result = Normalize(profile);

        Assert.Same(profile, result.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Sample_interval_below_one_second_is_raised()
    {
        var result = Normalize(Profile(sampleIntervalMs: 500));

        Assert.Equal(1000, result.Value.Monitoring.SampleIntervalMs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains("sampleIntervalMs", warning.Detail);
    }

    [Fact]
    public void Close_timeout_below_one_second_is_raised()
    {
        var result = Normalize(Profile(closeTimeoutMs: 0));

        Assert.Equal(1000, result.Value.BackgroundApps.CloseTimeoutMs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains("closeTimeoutMs", warning.Detail);
    }

    [Theory]
    [InlineData(false, true, "snapshotBeforeChanges")]
    [InlineData(true, false, "verifyAfterRestore")]
    public void Mandatory_recovery_steps_cannot_be_disabled(bool snapshot, bool verify, string key)
    {
        var result = Normalize(Profile(recovery: new ProfileRecovery(snapshot, true, true, verify)));

        Assert.True(result.Value.Recovery.SnapshotBeforeChanges);
        Assert.True(result.Value.Recovery.VerifyAfterRestore);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains(key, warning.Detail);
        Assert.Contains("обязательно по модели безопасности", warning.Detail);
    }

    [Fact]
    public void Game_names_are_trimmed_without_exe_and_deduplicated()
    {
        var result = Normalize(Profile(games: ["cs2.exe", " CS2 ", @"a\b", ""]));

        Assert.Equal(["cs2"], result.Value.Games);
        AssertAllInvalidValue(result, 3);
    }

    [Fact]
    public void Protected_processes_and_games_are_removed_from_background_apps()
    {
        var result = Normalize(Profile(games: ["cs2"], userSelected: ["explorer", "chrome", "cs2"]));

        Assert.Equal(["chrome"], result.Value.BackgroundApps.UserSelected);
        AssertAllInvalidValue(result, 2);
    }

    [Fact]
    public void Protected_name_in_games_is_removed_with_a_warning()
    {
        // Иначе приоритет explorer повышался бы как приоритет игры.
        var result = Normalize(Profile(games: ["explorer", "stalcraft"]));

        Assert.Equal(["stalcraft"], result.Value.Games);
        AssertAllInvalidValue(result, 1);
        Assert.Contains("is protected", result.Warnings[0].Detail);
        Assert.Contains("games", result.Warnings[0].Detail);
    }

    [Fact]
    public void Enabled_optional_actions_are_cleaned()
    {
        var result = Normalize(Profile(enabledOptional: [ActionIds.GamePriority, " ", "GAME-PRIORITY"]));

        Assert.Equal([ActionIds.GamePriority], result.Value.EnabledOptionalActions);
        AssertAllInvalidValue(result, 2);
    }

    [Theory]
    [InlineData("RealTime")]
    [InlineData("Idle")]
    [InlineData("BelowNormal")]
    public void Unsupported_priority_removes_the_action(string value)
    {
        var result = Normalize(Profile(actions: ActionsWith(new ProfileActionEntry(ActionIds.GamePriority, ActionMode.Optional, true, value))));

        Assert.Null(result.Value.FindAction(ActionIds.GamePriority));
        AssertAllInvalidValue(result, 1);
    }

    [Theory]
    [InlineData("High", "High")]
    [InlineData("AboveNormal", "AboveNormal")]
    [InlineData("high", "High")]
    public void Supported_priority_is_kept(string value, string expected)
    {
        var result = Normalize(Profile(actions: ActionsWith(new ProfileActionEntry(ActionIds.GamePriority, ActionMode.Optional, true, value))));

        Assert.Equal(expected, result.Value.FindAction(ActionIds.GamePriority)?.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Missing_priority_defaults_to_above_normal()
    {
        var result = Normalize(Profile(actions: ActionsWith(new ProfileActionEntry(ActionIds.GamePriority, ActionMode.Optional))));

        Assert.Equal("AboveNormal", result.Value.FindAction(ActionIds.GamePriority)?.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Power_plan_without_valid_scheme_is_removed()
    {
        var result = Normalize(Profile(actions: ActionsWith(
            new ProfileActionEntry(ActionIds.PowerPlan, ActionMode.AskFirst, true, TargetSchemes: ["not-a-guid"]))));

        Assert.Null(result.Value.FindAction(ActionIds.PowerPlan));
        Assert.NotEmpty(result.Warnings);
        Assert.All(result.Warnings, w => Assert.Equal(ConfigWarningKind.InvalidValue, w.Kind));
    }

    [Fact]
    public void Invalid_scheme_is_dropped_and_valid_one_kept()
    {
        var result = Normalize(Profile(actions: ActionsWith(
            new ProfileActionEntry(ActionIds.PowerPlan, ActionMode.AskFirst, true, TargetSchemes: ["not-a-guid", HighPerformance]))));

        Assert.Equal([HighPerformance], result.Value.FindAction(ActionIds.PowerPlan)?.TargetSchemes);
        AssertAllInvalidValue(result, 1);
    }

    [Fact]
    public void Duplicate_action_keeps_the_first_entry()
    {
        ProfileActionEntry[] actions =
        [
            new(ActionIds.PowerPlan, ActionMode.AskFirst, true, TargetSchemes: [HighPerformance]),
            new(ActionIds.PowerPlan, ActionMode.Allowed, true, TargetSchemes: [HighPerformance]),
        ];

        var result = Normalize(Profile(actions: actions));

        Assert.Equal(ActionMode.AskFirst, Assert.Single(result.Value.Actions).Mode);
        AssertAllInvalidValue(result, 1);
    }

    [Fact]
    public void Allowed_mode_is_left_to_ActionPolicy()
    {
        var result = Normalize(Profile(actions: ActionsWith(
            new ProfileActionEntry(ActionIds.PowerPlan, ActionMode.Allowed, true, TargetSchemes: [HighPerformance]))));

        Assert.Equal(ActionMode.Allowed, result.Value.FindAction(ActionIds.PowerPlan)?.Mode);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("EXPLORER.EXE", true)]
    [InlineData(" MsMpEng ", true)]
    [InlineData("easyanticheat_eos", true)]
    [InlineData("Booster.App", true)]
    [InlineData("chrome", false)]
    [InlineData("", false)]
    public void Protected_process_check_ignores_case_and_exe(string name, bool expected)
    {
        Assert.Equal(expected, ProtectedProcesses.IsProtected(name));
    }

    [Theory]
    [InlineData("cs2.exe", "cs2")]
    [InlineData("  Game.EXE ", "Game")]
    [InlineData("game.bin", "game.bin")]
    [InlineData(@"C:\Games\cs2.exe", null)]
    [InlineData("a/b", null)]
    [InlineData("a:b", null)]
    [InlineData(".exe", null)]
    [InlineData("..", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Process_name_is_normalized_or_rejected(string? input, string? expected)
    {
        Assert.Equal(expected, ProcessNames.Normalize(input));
    }
}
