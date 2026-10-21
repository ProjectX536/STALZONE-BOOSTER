using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.FakePowerPlanProvider;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles.Actions;

public class PowerPlanActionTests
{
    private static readonly PowerScheme Ultimate = new(Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61"), "Максимальная производительность");

    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PowerPlanAction Action(FakePowerPlanProvider power, params PowerScheme[] targets) =>
        new(power, (targets.Length == 0 ? [HighPerformance] : targets).Select(s => s.Id).ToArray(), _time);

    private static Guid Scheme(System.Text.Json.JsonElement value) => Guid.Parse(value.GetProperty("scheme").GetString()!);

    private static ChangeEntry Entry(ActionSnapshot snapshot) => Change(ActionIds.PowerPlan) with { Snapshot = snapshot };

    /// <summary>Как ActionExecutor: проверка, снимок, применение.</summary>
    private static async Task<ActionSnapshot> ApplyAsync(PowerPlanAction action)
    {
        Assert.True((await action.CheckSupportAsync(Ct)).IsSupported);
        var snapshot = await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);
        return snapshot;
    }

    [Fact]
    public async Task First_listed_target_missing_on_this_pc_takes_the_next()
    {
        var power = new FakePowerPlanProvider(Balanced, HighPerformance);
        var action = Action(power, Ultimate, HighPerformance);

        Assert.True((await action.CheckSupportAsync(Ct)).IsSupported);
        var snapshot = await action.CaptureAsync(Ct);

        Assert.Equal(HighPerformance.Id, Scheme(snapshot.AppliedValue!.Value));
    }

    [Theory]
    [InlineData("target-active")]
    [InlineData("no-target")]
    [InlineData("battery")]
    [InlineData("power-unknown")]
    [InlineData("active-unknown")]
    public async Task Unsupported_case_is_skipped_with_its_reason(string situation)
    {
        var power = new FakePowerPlanProvider(Balanced, HighPerformance);
        string expected;
        switch (situation)
        {
            case "target-active":
                power.Active = HighPerformance.Id;
                expected = ReportStrings.Skip_SchemeAlreadyActive;
                break;
            case "no-target":
                power.Remove(HighPerformance.Id);
                expected = ReportStrings.Skip_NoSuitableScheme;
                break;
            case "battery":
                power.Source = PowerSource.Battery;
                expected = ReportStrings.Skip_OnBattery;
                break;
            case "power-unknown":
                power.Source = PowerSource.Unknown;
                expected = ReportStrings.Skip_PowerSourceUnknown;
                break;
            default:
                // Активную схему не найти в списке — вернуть её потом было бы нечем.
                power.Active = Guid.NewGuid();
                expected = ReportStrings.Skip_ActiveSchemeUnknown;
                break;
        }

        var support = await Action(power).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(expected, support.Reason);
        Assert.Empty(power.SetCalls);
    }

    [Fact]
    public async Task Summary_names_both_schemes()
    {
        var support = await Action(new FakePowerPlanProvider(Balanced, HighPerformance)).CheckSupportAsync(Ct);

        Assert.Equal("«Сбалансированная» → «Высокая производительность»", support.Summary);
    }

    [Fact]
    public async Task Capture_records_ids_and_names_of_both_schemes()
    {
        var action = Action(new FakePowerPlanProvider(Balanced, HighPerformance));

        var snapshot = await action.CaptureAsync(Ct);

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(ActionIds.PowerPlan, snapshot.ActionId);
        Assert.Equal(Start, snapshot.CapturedAt);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", snapshot.OriginalValue.GetProperty("scheme").GetString());
        Assert.Equal("Сбалансированная", snapshot.OriginalValue.GetProperty("name").GetString());
        Assert.Equal(ProfileFactory.HighPerformance, snapshot.AppliedValue!.Value.GetProperty("scheme").GetString());
        Assert.Equal("Высокая производительность", snapshot.AppliedValue.Value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Only_apply_switches_the_scheme_and_exactly_once()
    {
        var power = new FakePowerPlanProvider(Balanced, HighPerformance);
        var action = Action(power);

        await action.CheckSupportAsync(Ct);
        await action.CaptureAsync(Ct);
        Assert.Empty(power.SetCalls);

        await action.ApplyAsync(Ct);

        Assert.Equal([HighPerformance.Id], power.SetCalls);
    }

    [Fact]
    public async Task Apply_without_capture_changes_nothing()
    {
        var power = new FakePowerPlanProvider(Balanced, HighPerformance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(power).ApplyAsync(Ct));

        Assert.Empty(power.SetCalls);
    }

    [Theory]
    [InlineData("original", ObservedState.Original)]
    [InlineData("applied", ObservedState.Applied)]
    [InlineData("other", ObservedState.Other)]
    public async Task Inspect_compares_active_scheme_with_snapshot(string active, ObservedState expected)
    {
        var power = new FakePowerPlanProvider(Balanced, HighPerformance, PowerSaver);
        var snapshot = await Action(power).CaptureAsync(Ct);
        power.Active = active switch
        {
            "original" => Balanced.Id,
            "applied" => HighPerformance.Id,
            _ => PowerSaver.Id,
        };

        Assert.Equal(expected, await new PowerPlanRestorer(power).InspectAsync(snapshot, Ct));
    }

    [Fact]
    public async Task User_scheme_is_restored_by_its_id()
    {
        var powerX = new PowerScheme(Guid.NewGuid(), "PowerX v2");
        var power = new FakePowerPlanProvider(powerX, Balanced, HighPerformance);
        var snapshot = await ApplyAsync(Action(power));
        var rollback = new RollbackManager([new PowerPlanRestorer(power)], new RecordingJournal(), _time, NullLogger<RollbackManager>.Instance);

        var restored = await rollback.RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.Restored, restored.Changes[0].Status);
        Assert.Equal([HighPerformance.Id, powerX.Id], power.SetCalls);
        Assert.Equal(powerX.Id, power.Active);
    }

    [Fact]
    public async Task Deleted_original_scheme_fails_its_restore_and_others_are_restored()
    {
        var powerX = new PowerScheme(Guid.NewGuid(), "PowerX v2");
        var power = new FakePowerPlanProvider(powerX, Balanced, HighPerformance);
        var snapshot = await ApplyAsync(Action(power));
        power.Remove(powerX.Id);
        var state = new FakeSystemState { ["other"] = "boost" };
        var rollback = new RollbackManager([new PowerPlanRestorer(power), new FakeRestorer(state, "other")], new RecordingJournal(), _time,
            NullLogger<RollbackManager>.Instance);

        // Обратный порядок: схема восстанавливается первой, её сбой не мешает следующему.
        var restored = await rollback.RestoreAsync(Session(changes: [Change("other"), Entry(snapshot)]), Ct);

        Assert.Equal([ChangeStatus.Restored, ChangeStatus.RestoreFailed], restored.Changes.Select(c => c.Status));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ReportStrings.Restore_OriginalSchemeDeletedFormat, "PowerX v2"),
            restored.Changes[1].Detail);
        Assert.Equal("orig", state["other"]);
        Assert.Equal([HighPerformance.Id], power.SetCalls);
    }

    [Fact]
    public async Task Scheme_chosen_by_user_during_game_is_a_conflict()
    {
        var power = new FakePowerPlanProvider(PowerSaver, Balanced, HighPerformance);
        var snapshot = await ApplyAsync(Action(power));
        power.Active = Balanced.Id;
        var rollback = new RollbackManager([new PowerPlanRestorer(power)], new RecordingJournal(), _time, NullLogger<RollbackManager>.Instance);

        var restored = await rollback.RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.RestoreConflict, restored.Changes[0].Status);
        Assert.Equal([HighPerformance.Id], power.SetCalls);
        Assert.Equal(Balanced.Id, power.Active);
    }

    [Fact]
    public void Allowed_mode_still_needs_confirmation_in_autostart()
    {
        var descriptor = Action(new FakePowerPlanProvider()).Descriptor;

        var decision = new ActionPolicy().Evaluate(descriptor, ActionMode.Allowed,
            new PolicyContext(ExecutionTrigger.Automatic, IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: false, CanElevate: false));

        Assert.Equal(new PolicyDecision(PolicyVerdict.RequireConfirmation, PolicyReason.NotVerifiedForAutomatic), decision);
    }

    [Fact]
    public void Ask_first_confirmed_by_user_is_allowed()
    {
        var descriptor = Action(new FakePowerPlanProvider()).Descriptor;

        var decision = new ActionPolicy().Evaluate(descriptor, ActionMode.AskFirst,
            new PolicyContext(ExecutionTrigger.UserInitiated, IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: true, CanElevate: false));

        Assert.Equal(PolicyVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void Descriptor_is_reversible_level_one_without_admin()
    {
        var descriptor = Action(new FakePowerPlanProvider()).Descriptor;

        Assert.Equal(new ActionDescriptor(ActionIds.PowerPlan, ReportStrings.Action_PowerPlan, SafetyLevel.L1Reversible,
            RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: false, IsReversible: true,
            AllowedDuringGaming: true, VerifiedForAutomatic: false), descriptor);
    }
}
