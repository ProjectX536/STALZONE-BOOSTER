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
    private readonly FakeUserRegistry _machine = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PolicyContext Context(bool confirmed, bool elevated) => new(ExecutionTrigger.UserInitiated,
        IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: confirmed, CanElevate: elevated);

    private StartupItemAction Action(StartupEntry? entry = null) => new(_registry, _machine, entry ?? Spotify, _time);

    private Task<ExecutionResult> Execute(StartupItemAction action, bool confirmed, bool elevated = false) =>
        new ActionExecutor(new ActionPolicy(), _journal, _time, NullLogger<ActionExecutor>.Instance)
            .ExecuteAsync(Session(), action, "s", ActionMode.AskFirst, Context(confirmed, elevated), Ct);

    private Task<SessionRecord> Restore(SessionRecord session) =>
        new RollbackManager([new StartupItemRestorer(_registry), new MachineStartupItemRestorer(_machine)], _journal, _time,
            NullLogger<RollbackManager>.Instance).RestoreAsync(session, Ct);

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
    [InlineData(StartupLocation.UserRunOnce)]
    [InlineData(StartupLocation.MachineRunOnce)]
    public async Task Run_once_item_is_not_supported(StartupLocation location)
    {
        var support = await Action(Spotify with { Location = location }).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_StartupItemRunOnce, support.Reason);
        Assert.Equal((0, 0, 0, 0), (_registry.Reads, _registry.Writes, _machine.Reads, _machine.Writes));
    }

    public static TheoryData<StartupLocation, string> MachineLocations => new()
    {
        { StartupLocation.MachineRun, StartupApprovedKeys.Run },
        { StartupLocation.MachineRun32, StartupApprovedKeys.Run32 },
        { StartupLocation.CommonFolder, StartupApprovedKeys.StartupFolder },
    };

    [Theory]
    [MemberData(nameof(MachineLocations))]
    public async Task Machine_item_is_disabled_in_machine_startup_approved_and_restored(StartupLocation location, string key)
    {
        _machine[key, "Steam"] = Enabled;
        var action = Action(Spotify with { Name = "Steam", Location = location });

        var result = await Execute(action, confirmed: true, elevated: true);

        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.Equal(UserRegistryValue.FromBinary(StartupApproval.Disabled(Start)), _machine[key, "Steam"]);
        Assert.Equal((0, 0), (_registry.Reads, _registry.Writes));

        var restored = await Restore(result.Session);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(restored.Changes).Status);
        Assert.Equal(Enabled, _machine[key, "Steam"]);
    }

    [Fact]
    public async Task Machine_item_is_refused_without_elevation()
    {
        var result = await Execute(Action(Spotify with { Location = StartupLocation.MachineRun }), confirmed: true, elevated: false);

        Assert.Equal(ExecutionStatus.Refused, result.Status);
        Assert.Equal(PolicyReason.RequiresAdmin, result.Decision.Reason);
        Assert.Equal((0, 0), (_machine.Writes, _machine.Deletes));
    }

    [Fact]
    public void Machine_descriptor_requires_admin()
    {
        Assert.Equal(new ActionDescriptor(ActionIds.MachineStartupItem, ReportStrings.Action_MachineStartupItem, SafetyLevel.L2Confirmed,
                RequiresAdmin: true, RequiresReboot: false, RequiresConfirmation: true, IsReversible: true, AllowedDuringGaming: false,
                VerifiedForAutomatic: false),
            Action(Spotify with { Location = StartupLocation.CommonFolder }).Descriptor);
    }

    [Fact]
    public async Task Machine_restore_without_admin_rights_fails_with_reason_and_can_be_retried()
    {
        _machine[StartupApprovedKeys.Run, "Steam"] = Enabled;
        var result = await Execute(Action(Spotify with { Name = "Steam", Location = StartupLocation.MachineRun }), confirmed: true,
            elevated: true);
        // Booster перезапущен без прав администратора: запись в HKLM отклоняет Windows.
        _machine.WriteFailure = new UnauthorizedAccessException("Access to the registry key is denied.");

        var failed = Assert.Single((await Restore(result.Session)).Changes);

        Assert.Equal(ChangeStatus.RestoreFailed, failed.Status);
        Assert.Equal(ReportStrings.Restore_RegistryAccessDenied, failed.Detail);

        _machine.WriteFailure = null;
        Assert.Equal(ChangeStatus.Restored, Assert.Single((await Restore(result.Session with { Changes = [failed] })).Changes).Status);
        Assert.Equal(Enabled, _machine[StartupApprovedKeys.Run, "Steam"]);
    }

    [Fact]
    public async Task Machine_restorer_refuses_keys_outside_startup_approved()
    {
        var evil = UserRegistrySnapshot.Capture(ActionIds.MachineStartupItem, @"Software\Microsoft\Windows\CurrentVersion\Run", "Steam", null,
            UserRegistryValue.FromString("evil.exe"), Start);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new MachineStartupItemRestorer(_machine).RevertAsync(evil, Ct));
        Assert.Equal((0, 0, 0), (_machine.Reads, _machine.Writes, _machine.Deletes));
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
