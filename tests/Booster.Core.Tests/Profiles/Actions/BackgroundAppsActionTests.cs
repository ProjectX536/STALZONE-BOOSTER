using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles.Actions;

public class BackgroundAppsActionTests
{
    private const int GamePid = 4242;

    private readonly FakeProcessProvider _processes = new();
    private readonly FakeAppCloser _closer = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GameProcess StartGame() => new("cs2", _processes.Start("cs2", GamePid));

    private void Run(params string[] names)
    {
        var pid = 100;
        foreach (var name in names)
            _processes.Start(name, pid += 4);
    }

    private static GamingProfile Profile(params string[] userSelected) =>
        ProfileFactory.Profile(games: ["cs2", "dota2"], userSelected: userSelected, closeTimeoutMs: 7000);

    private BackgroundAppsAction Action(GamingProfile profile, GameProcess? game) => new(_processes, _closer, profile, game, _time);

    [Fact]
    public async Task Game_protected_and_own_processes_are_never_closed()
    {
        var game = StartGame();
        Run("explorer", "Booster.App", "chrome", "dota2");
        var action = Action(Profile("cs2", "explorer", "Booster.App", "chrome", "dota2", "CS2.exe"), game);

        var support = await action.CheckSupportAsync(Ct);
        await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);

        Assert.Equal("Закрыть: chrome", support.Summary);
        Assert.Equal(["chrome"], _closer.Requested);
    }

    [Fact]
    public async Task Apps_that_are_not_running_are_left_out()
    {
        Run("Discord");
        var action = Action(Profile("chrome", "Discord"), StartGame());

        var support = await action.CheckSupportAsync(Ct);
        var snapshot = await action.CaptureAsync(Ct);

        Assert.Equal("Закрыть: Discord", support.Summary);
        Assert.Equal(["Discord"], snapshot.OriginalValue.GetProperty("apps").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task Nothing_selected_running_is_not_supported()
    {
        var support = await Action(Profile("chrome", "Discord"), StartGame()).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_NoSelectedAppRunning, support.Reason);
    }

    [Fact]
    public async Task Nothing_left_to_close_after_exclusions_is_not_supported()
    {
        Run("explorer");

        var support = await Action(Profile("explorer", "cs2"), StartGame()).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_NoAppsSelected, support.Reason);
    }

    [Fact]
    public async Task Running_app_that_was_not_selected_is_never_closed()
    {
        Run("chrome", "Telegram");
        var action = Action(Profile("chrome"), StartGame());

        await action.CheckSupportAsync(Ct);
        await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);

        Assert.Equal(["chrome"], _closer.Requested);
    }

    [Fact]
    public async Task Each_app_gets_the_profile_close_timeout()
    {
        Run("chrome", "Discord");
        var action = Action(Profile("chrome", "Discord"), StartGame());

        await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);

        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], _closer.Timeouts);
    }

    [Fact]
    public async Task App_still_running_is_reported_not_failed()
    {
        Run("chrome", "Discord", "Steam");
        _closer.Results["Discord"] = CloseRequestResult.StillRunning;
        _closer.Results["Steam"] = CloseRequestResult.NoWindow;
        var action = Action(Profile("chrome", "Discord", "Steam"), StartGame());
        await action.CaptureAsync(Ct);

        var result = await action.ApplyAsync(Ct);

        Assert.Equal("""{"closed":["chrome"],"stillRunning":["Discord"],"noWindow":["Steam"],"failed":[],"notRunning":[]}""",
            result.Data!.Value.GetRawText());
        Assert.Equal("Закрыто: chrome; не закрылось: Discord, Steam. Закрытые приложения не перезапускаются", result.Detail);
    }

    [Fact]
    public async Task Failure_on_one_app_does_not_stop_the_others()
    {
        Run("chrome", "Discord", "Spotify");
        _closer.Failing.Add("Discord");
        _closer.Results["Spotify"] = CloseRequestResult.NotRunning;
        var action = Action(Profile("chrome", "Discord", "Spotify"), StartGame());
        await action.CaptureAsync(Ct);

        var result = await action.ApplyAsync(Ct);

        Assert.Equal(["chrome", "Discord", "Spotify"], _closer.Requested);
        Assert.Equal("""{"closed":["chrome"],"stillRunning":[],"noWindow":[],"failed":["Discord"],"notRunning":["Spotify"]}""",
            result.Data!.Value.GetRawText());
        Assert.Equal("Закрыто: chrome; не закрылось: Discord. Закрытые приложения не перезапускаются", result.Detail);
    }

    [Fact]
    public async Task Cancellation_stops_closing()
    {
        Run("chrome", "Discord");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _closer.OnRequest = _ => cancel.Cancel();
        var action = Action(Profile("chrome", "Discord"), StartGame());
        await action.CaptureAsync(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.ApplyAsync(cancel.Token));

        Assert.Equal(["chrome"], _closer.Requested);
    }

    [Fact]
    public async Task Before_the_game_running_selected_apps_are_closed_except_the_profile_games()
    {
        Run("chrome", "Discord", "dota2", "explorer");
        var action = Action(Profile("chrome", "explorer", "Discord", "dota2", "Steam"), null);

        var support = await action.CheckSupportAsync(Ct);
        await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);

        Assert.True(support.IsSupported);
        Assert.Equal("Закрыть: chrome, Discord", support.Summary);
        Assert.Equal(["chrome", "Discord"], _closer.Requested);
    }

    [Fact]
    public async Task Before_the_game_nothing_selected_running_is_not_supported()
    {
        Run("Steam");

        var support = await Action(Profile("chrome", "Discord"), null).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal("ни одно из выбранных приложений не запущено", support.Reason);
    }

    [Fact]
    public async Task Capture_lists_the_apps_to_close()
    {
        Run("chrome", "Discord");

        var snapshot = await Action(Profile("chrome", "Discord"), StartGame()).CaptureAsync(Ct);

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(ActionIds.BackgroundApps, snapshot.ActionId);
        Assert.Equal(Start, snapshot.CapturedAt);
        Assert.Equal("""{"apps":["chrome","Discord"]}""", snapshot.OriginalValue.GetRawText());
        Assert.Equal("""{"apps":["chrome","Discord"]}""", snapshot.AppliedValue!.Value.GetRawText());
    }

    [Fact]
    public async Task Apply_without_capture_closes_nothing()
    {
        Run("chrome");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(Profile("chrome"), StartGame()).ApplyAsync(Ct));

        Assert.Empty(_closer.Requested);
    }

    [Fact]
    public async Task Confirmed_closing_is_not_done_once_the_game_runs()
    {
        Run("chrome");
        var game = StartGame();
        var profile = ProfileFactory.Profile(games: ["cs2"], userSelected: ["chrome"],
            actions: [new ProfileActionEntry(ActionIds.BackgroundApps, ActionMode.AskFirst, Reversible: false)]);
        var journal = new RecordingJournal();
        var policy = new ActionPolicy();
        var manager = new GamingProfileManager(new FakeActionCatalog().Add(Action(profile, game)), policy,
            new ActionExecutor(policy, journal, _time, NullLogger<ActionExecutor>.Instance),
            new RollbackManager([], journal, _time, NullLogger<RollbackManager>.Instance),
            new RecoveryValidator([], journal, _time, NullLogger<RecoveryValidator>.Instance),
            journal, _processes, new FakeSystemLoad(), new FakeSensorProvider(_time),
            new SessionFrameCapture(new FakeFrameCaptureProvider(_time), new FakeConfiguration(), NullLogger<SessionFrameCapture>.Instance),
            new FakeConfiguration(), _time, NullLogger<GamingProfileManager>.Instance);
        var plan = await manager.PreflightAsync(profile, game, ExecutionTrigger.UserInitiated,
            new HashSet<string>(StringComparer.Ordinal) { ActionIds.BackgroundApps }, Ct);

        var session = await manager.RunAsync(plan, Ct);

        Assert.Equal(ReportStrings.Skip_OnlyBeforeGame, Assert.Single(plan.Actions).SkipReason);
        Assert.Null(session);
        Assert.Empty(journal.Saves);
        Assert.Empty(_closer.Requested);
    }

    [Fact]
    public void Allowed_mode_still_needs_confirmation_in_autostart()
    {
        var descriptor = Action(Profile("chrome"), null).Descriptor;

        var decision = new ActionPolicy().Evaluate(descriptor, ActionMode.Allowed,
            new PolicyContext(ExecutionTrigger.Automatic, IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: false, CanElevate: false));

        Assert.Equal(PolicyVerdict.RequireConfirmation, decision.Verdict);
    }

    [Fact]
    public void Ask_first_confirmed_by_user_is_allowed()
    {
        var descriptor = Action(Profile("chrome"), null).Descriptor;

        var decision = new ActionPolicy().Evaluate(descriptor, ActionMode.AskFirst,
            new PolicyContext(ExecutionTrigger.UserInitiated, IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: true, CanElevate: false));

        Assert.Equal(PolicyVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void Descriptor_is_confirmed_irreversible_and_only_before_the_game()
    {
        Assert.Equal(new ActionDescriptor(ActionIds.BackgroundApps, ReportStrings.Action_BackgroundApps, SafetyLevel.L2Confirmed,
            RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: true, IsReversible: false,
            AllowedDuringGaming: false, VerifiedForAutomatic: false), Action(Profile("chrome"), null).Descriptor);
    }
}
