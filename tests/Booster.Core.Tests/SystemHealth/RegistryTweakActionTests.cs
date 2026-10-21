using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.SystemHealth;

public class RegistryTweakActionTests
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GamePath = @"D:\Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";

    private readonly FakeUserRegistry _registry = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RegistryTweakDefinition Definition(string id) =>
        Assert.Single(FakeConfiguration.DefaultCatalog.RegistryTweaks, d => d.Id == id);

    private RegistryTweakAction GameMode() =>
        new(_registry, Definition(RegistryTweakIds.GameMode), "AutoGameModeEnabled", _time);

    private RegistryTweakAction Pair(string id) => new(_registry, Definition(id), GamePath, _time);

    private static PolicyContext Context(bool confirmed) => new(ExecutionTrigger.UserInitiated, IsGamingSessionActive: false,
        UserEnabled: false, UserConfirmed: confirmed, CanElevate: false);

    private Task<ExecutionResult> Execute(RegistryTweakAction action, string sessionId = Id) =>
        new ActionExecutor(new ActionPolicy(), _journal, _time, NullLogger<ActionExecutor>.Instance)
            .ExecuteAsync(Session(sessionId), action, "s", ActionMode.AskFirst, Context(confirmed: true), Ct);

    private RegistryTweakRestorer Restorer() => new(_registry, new FakeConfiguration());

    private Task<SessionRecord> Restore(SessionRecord session) =>
        new RollbackManager([Restorer()], _journal, _time, NullLogger<RollbackManager>.Instance).RestoreAsync(session, Ct);

    [Fact]
    public async Task Game_mode_is_set_and_restored()
    {
        _registry[GameBar, "AutoGameModeEnabled"] = UserRegistryValue.FromDWord(0);
        var action = GameMode();

        var support = await action.CheckSupportAsync(Ct);
        var result = await Execute(action);

        Assert.True(support.IsSupported);
        Assert.Equal("Игровой режим Windows: 0 → 1", support.Summary);
        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.Equal(UserRegistryValue.FromDWord(1), _registry[GameBar, "AutoGameModeEnabled"]);

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(restored.Changes).Status);
        Assert.Equal(UserRegistryValue.FromDWord(0), _registry[GameBar, "AutoGameModeEnabled"]);
    }

    [Fact]
    public async Task Pair_is_added_and_absent_value_deleted_on_revert()
    {
        var action = Pair(RegistryTweakIds.GpuHighPerformance);

        var support = await action.CheckSupportAsync(Ct);
        var result = await Execute(action);

        Assert.Equal("Высокая производительность GPU для игры: нет → GpuPreference=2;", support.Summary);
        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.Equal(UserRegistryValue.FromString("GpuPreference=2;"), _registry[GpuPreferences, GamePath]);

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(restored.Changes).Status);
        Assert.Null(_registry[GpuPreferences, GamePath]);
        Assert.Equal(1, _registry.Deletes);
    }

    [Fact]
    public async Task Second_pair_keeps_first_and_out_of_order_revert_conflicts()
    {
        var first = await Execute(Pair(RegistryTweakIds.GpuHighPerformance), "first");
        var second = await Execute(Pair(RegistryTweakIds.WindowedFlipModel), "second");

        Assert.Equal(UserRegistryValue.FromString("GpuPreference=2;SwapEffectUpgradeEnable=1;"), _registry[GpuPreferences, GamePath]);

        var firstRestored = await Restore(first.Session);
        Assert.Equal(ChangeStatus.RestoreConflict, Assert.Single(firstRestored.Changes).Status);
        Assert.Equal(UserRegistryValue.FromString("GpuPreference=2;SwapEffectUpgradeEnable=1;"), _registry[GpuPreferences, GamePath]);

        var secondRestored = await Restore(second.Session);
        Assert.Equal(ChangeStatus.Restored, Assert.Single(secondRestored.Changes).Status);
        Assert.Equal(UserRegistryValue.FromString("GpuPreference=2;"), _registry[GpuPreferences, GamePath]);
    }

    [Fact]
    public async Task Already_set_pair_is_not_supported()
    {
        _registry[GpuPreferences, GamePath] = UserRegistryValue.FromString("SwapEffectUpgradeEnable=1;GpuPreference=2;");

        var support = await Pair(RegistryTweakIds.GpuHighPerformance).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_RegistryTweakAlreadySet, support.Reason);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public async Task Game_mode_already_on_is_not_supported()
    {
        _registry[GameBar, "AutoGameModeEnabled"] = UserRegistryValue.FromDWord(1);

        var support = await GameMode().CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_RegistryTweakAlreadyEnabled, support.Reason);
    }

    [Fact]
    public async Task Value_of_unexpected_type_is_not_supported()
    {
        _registry[GpuPreferences, GamePath] = UserRegistryValue.FromDWord(2);
        _registry[GameBar, "AutoGameModeEnabled"] = UserRegistryValue.FromString("0");

        var pair = await Pair(RegistryTweakIds.GpuHighPerformance).CheckSupportAsync(Ct);
        var gameMode = await GameMode().CheckSupportAsync(Ct);
        _registry.ReadFailure = new InvalidDataException("REG_QWORD");
        var unreadable = await GameMode().CheckSupportAsync(Ct);

        Assert.All([pair, gameMode, unreadable], s =>
        {
            Assert.False(s.IsSupported);
            Assert.Equal(ReportStrings.Skip_RegistryTweakUnexpectedType, s.Reason);
        });
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void Descriptor_is_confirmed_reversible_and_not_for_gaming()
    {
        Assert.Equal(new ActionDescriptor(ActionIds.RegistryTweak, ReportStrings.Action_RegistryTweak_WindowedFlipModel,
                SafetyLevel.L2Confirmed, RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: true, IsReversible: true,
                AllowedDuringGaming: false, VerifiedForAutomatic: false),
            Pair(RegistryTweakIds.WindowedFlipModel).Descriptor);
        Assert.Equal(ReportStrings.Action_RegistryTweak_GameMode, GameMode().Descriptor.DisplayName);
        Assert.Equal(ReportStrings.Action_RegistryTweak_GpuHighPerformance, Pair(RegistryTweakIds.GpuHighPerformance).Descriptor.DisplayName);
    }

    [Fact]
    public void Definition_outside_known_ids_is_rejected()
    {
        var unknown = new RegistryTweakDefinition("network-throttling", GameBar, "Unknown", "1", 0);

        Assert.Throws<ArgumentException>(() => new RegistryTweakAction(_registry, unknown, "Unknown", _time));
    }

    [Theory]
    [InlineData(StartupApprovedKeys.Run, "Spotify")]
    [InlineData(GameBar, "ShowStartupPanel")]
    [InlineData(GpuPreferences, "")]
    [InlineData(@"Software\Microsoft\DirectX\UserGpuPreferences\Sub", GamePath)]
    public async Task Restorer_rejects_snapshot_key_missing_from_catalog(string subKey, string valueName)
    {
        var evil = UserRegistrySnapshot.Capture(ActionIds.RegistryTweak, subKey, valueName, null, UserRegistryValue.FromDWord(1), Start);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Restorer().RevertAsync(evil, Ct));
        Assert.Equal((0, 0, 0), (_registry.Reads, _registry.Writes, _registry.Deletes));
    }
}
