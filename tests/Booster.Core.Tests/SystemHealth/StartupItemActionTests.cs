using Booster.Core.Abstractions;
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

public class StartupItemActionTests
{
    private static readonly UserRegistryValue Enabled = UserRegistryValue.FromBinary([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    private static readonly StartupEntry Spotify = new("Spotify", StartupLocation.UserRun, @"""C:\Spotify\Spotify.exe"" /minimized",
        @"C:\Spotify\Spotify.exe", StartupState.Enabled);

    private readonly FakeUserRegistry _registry = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PolicyContext Context(bool confirmed) => new(ExecutionTrigger.UserInitiated, IsGamingSessionActive: false,
        UserEnabled: false, UserConfirmed: confirmed, CanElevate: false);

    private StartupItemAction Action(StartupEntry? entry = null) => new(_registry, entry ?? Spotify, _time);

    private Task<ExecutionResult> Execute(StartupItemAction action, bool confirmed) =>
        new ActionExecutor(new ActionPolicy(), _journal, _time, NullLogger<ActionExecutor>.Instance)
            .ExecuteAsync(Session(), action, "s", ActionMode.AskFirst, Context(confirmed), Ct);

    private Task<SessionRecord> Restore(SessionRecord session) =>
        new RollbackManager([new StartupItemRestorer(_registry)], _journal, _time, NullLogger<RollbackManager>.Instance)
            .RestoreAsync(session, Ct);

    [Fact]
    public async Task Disable_writes_task_manager_value_and_restorer_returns_original()
    {
        _registry[StartupApprovedKeys.Run, "Spotify"] = Enabled;
        var action = Action();

        var support = await action.CheckSupportAsync(Ct);
        var result = await Execute(action, confirmed: true);

        Assert.True(support.IsSupported);
        Assert.Equal("Spotify: отключить автозапуск (со следующего входа в Windows)", support.Summary);
        Assert.Equal(ExecutionStatus.Applied, result.Status);
        var written = _registry[StartupApprovedKeys.Run, "Spotify"];
        Assert.Equal(UserRegistryValue.FromBinary(StartupApproval.Disabled(Start)), written);
        Assert.Equal(StartupState.Disabled, StartupApproval.Parse(written!.ToBytes()));

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(restored.Changes).Status);
        Assert.Equal(Enabled, _registry[StartupApprovedKeys.Run, "Spotify"]);
    }

    [Fact]
    public async Task Absent_original_is_deleted_on_revert()
    {
        var folderItem = new StartupEntry("Teams.lnk", StartupLocation.UserFolder, @"C:\Startup\Teams.lnk", @"C:\Teams\ms-teams.exe",
            StartupState.Enabled);

        var result = await Execute(Action(folderItem), confirmed: true);

        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.NotNull(_registry[StartupApprovedKeys.StartupFolder, "Teams.lnk"]);
        Assert.Null(_registry[StartupApprovedKeys.Run, "Teams.lnk"]);

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(restored.Changes).Status);
        Assert.Null(_registry[StartupApprovedKeys.StartupFolder, "Teams.lnk"]);
        Assert.Equal(1, _registry.Deletes);
    }

    [Fact]
    public async Task User_change_after_disable_is_restore_conflict()
    {
        var result = await Execute(Action(), confirmed: true);
        // Пользователь снова включил элемент в диспетчере задач.
        _registry[StartupApprovedKeys.Run, "Spotify"] = Enabled;

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.RestoreConflict, Assert.Single(restored.Changes).Status);
        Assert.Equal(Enabled, _registry[StartupApprovedKeys.Run, "Spotify"]);
    }

    [Fact]
    public async Task Already_disabled_item_is_not_supported()
    {
        _registry[StartupApprovedKeys.Run, "Spotify"] = UserRegistryValue.FromBinary(StartupApproval.Disabled(Start.AddDays(-1)));

        var support = await Action().CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_StartupItemAlreadyDisabled, support.Reason);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public async Task Value_of_unknown_format_is_not_supported()
    {
        _registry[StartupApprovedKeys.Run, "Spotify"] = UserRegistryValue.FromDWord(2);

        var support = await Action().CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_StartupItemStateUnknown, support.Reason);
    }

    [Theory]
    [InlineData(StartupLocation.MachineRun)]
    [InlineData(StartupLocation.MachineRun32)]
    [InlineData(StartupLocation.CommonFolder)]
    [InlineData(StartupLocation.UserRunOnce)]
    [InlineData(StartupLocation.MachineRunOnce)]
    public async Task Machine_item_is_not_supported(StartupLocation location)
    {
        var support = await Action(Spotify with { Location = location }).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_StartupItemNotUser, support.Reason);
        Assert.Equal((0, 0), (_registry.Reads, _registry.Writes));
    }

    [Fact]
    public async Task Executor_refuses_without_confirmation()
    {
        _registry[StartupApprovedKeys.Run, "Spotify"] = Enabled;
        var action = Action();

        var result = await Execute(action, confirmed: false);

        Assert.Equal(ExecutionStatus.Refused, result.Status);
        Assert.Equal(PolicyVerdict.RequireConfirmation, result.Decision.Verdict);
        Assert.Equal((0, 0), (_registry.Writes, _registry.Deletes));
        Assert.Equal(Enabled, _registry[StartupApprovedKeys.Run, "Spotify"]);
        Assert.Empty(_journal.Saves);
    }

    [Fact]
    public void Descriptor_is_confirmed_reversible_and_not_for_gaming()
    {
        Assert.Equal(new ActionDescriptor(ActionIds.StartupItem, ReportStrings.Action_StartupItem, SafetyLevel.L2Confirmed,
                RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: true, IsReversible: true, AllowedDuringGaming: false,
                VerifiedForAutomatic: false),
            Action().Descriptor);
    }

    [Fact]
    public async Task Restorer_refuses_keys_outside_startup_approved()
    {
        var evil = UserRegistrySnapshot.Capture(ActionIds.StartupItem, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", null,
            UserRegistryValue.FromDWord(1), Start);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new StartupItemRestorer(_registry).RevertAsync(evil, Ct));
        Assert.Equal((0, 0, 0), (_registry.Reads, _registry.Writes, _registry.Deletes));
    }
}
