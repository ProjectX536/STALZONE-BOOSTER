using Booster.Core.Abstractions;
using Booster.Core.Engine;
using static Booster.Core.Abstractions.SafetyLevel;

namespace Booster.Core.Tests.Engine;

public class ActionPolicyTests
{
    private static readonly ActionPolicy Policy = new();

    private static ActionDescriptor D(
        SafetyLevel level,
        bool reversible = true,
        bool verified = false,
        bool admin = false,
        bool reboot = false,
        bool confirm = false,
        bool duringGaming = true) =>
        new("test.action", "Тест", level, admin, reboot, confirm, reversible, duringGaming, verified);

    private static PolicyContext Ctx(
        ExecutionTrigger trigger = ExecutionTrigger.Automatic,
        bool gaming = false,
        bool enabled = false,
        bool confirmed = false,
        bool canElevate = false) =>
        new(trigger, gaming, enabled, confirmed, canElevate);

    private const ExecutionTrigger User = ExecutionTrigger.UserInitiated;

    public static TheoryData<int, ActionMode?, ActionDescriptor, PolicyContext, PolicyVerdict, PolicyReason> Rows => new()
    {
        { 1, null, D(L0ReadOnly), Ctx(), PolicyVerdict.RecommendOnly, PolicyReason.UnknownAction },
        { 2, ActionMode.Forbidden, D(L0ReadOnly), Ctx(), PolicyVerdict.Deny, PolicyReason.Forbidden },
        { 3, ActionMode.Forbidden, D(L3ManualOnly), Ctx(), PolicyVerdict.Deny, PolicyReason.Forbidden },
        { 4, ActionMode.Allowed, D(L3ManualOnly), Ctx(confirmed: true), PolicyVerdict.RecommendOnly, PolicyReason.ManualOnlyLevel },
        { 5, ActionMode.RecommendOnly, D(L1Reversible, verified: true), Ctx(gaming: true), PolicyVerdict.RecommendOnly, PolicyReason.RecommendOnlyMode },
        { 6, ActionMode.Allowed, D(L1Reversible, verified: true, reboot: true), Ctx(gaming: true), PolicyVerdict.Deny, PolicyReason.NotDuringGaming },
        { 7, ActionMode.Allowed, D(L1Reversible, verified: true, duringGaming: false), Ctx(gaming: true), PolicyVerdict.Deny, PolicyReason.NotDuringGaming },
        { 8, ActionMode.Allowed, D(L1Reversible, verified: true, duringGaming: false), Ctx(), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 9, ActionMode.Allowed, D(L1Reversible, verified: true, admin: true), Ctx(canElevate: false), PolicyVerdict.Deny, PolicyReason.RequiresAdmin },
        { 10, ActionMode.Allowed, D(L1Reversible, verified: true, admin: true), Ctx(canElevate: true), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 11, ActionMode.Optional, D(L1Reversible, verified: true), Ctx(enabled: false), PolicyVerdict.NotEnabled, PolicyReason.OptionalNotEnabled },
        { 12, ActionMode.Optional, D(L1Reversible, verified: true), Ctx(enabled: true), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 13, ActionMode.Allowed, D(L0ReadOnly), Ctx(), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 14, ActionMode.Allowed, D(L1Reversible), Ctx(), PolicyVerdict.RequireConfirmation, PolicyReason.NotVerifiedForAutomatic },
        { 15, ActionMode.Allowed, D(L1Reversible), Ctx(User), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 16, ActionMode.Allowed, D(L1Reversible, verified: true, reversible: false), Ctx(), PolicyVerdict.RequireConfirmation, PolicyReason.Irreversible },
        { 17, ActionMode.Allowed, D(L1Reversible, reversible: false), Ctx(User), PolicyVerdict.RequireConfirmation, PolicyReason.Irreversible },
        { 18, ActionMode.Allowed, D(L1Reversible, reversible: false), Ctx(User, confirmed: true), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 19, ActionMode.Allowed, D(L2Confirmed), Ctx(), PolicyVerdict.RequireConfirmation, PolicyReason.ConfirmationRequired },
        { 20, ActionMode.Allowed, D(L2Confirmed), Ctx(confirmed: true), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 21, ActionMode.AskFirst, D(L0ReadOnly), Ctx(), PolicyVerdict.RequireConfirmation, PolicyReason.ConfirmationRequired },
        { 22, ActionMode.AskFirst, D(L1Reversible, verified: true), Ctx(User, confirmed: true), PolicyVerdict.Allow, PolicyReason.Allowed },
        { 23, ActionMode.Allowed, D(L1Reversible, verified: true, confirm: true), Ctx(), PolicyVerdict.RequireConfirmation, PolicyReason.ConfirmationRequired },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Evaluate_follows_decision_table(
        int row, ActionMode? mode, ActionDescriptor action, PolicyContext context, PolicyVerdict verdict, PolicyReason reason)
    {
        var decision = Policy.Evaluate(action, mode, context);

        Assert.True(
            new PolicyDecision(verdict, reason) == decision,
            $"Строка {row}: ожидалось {verdict}/{reason}, получено {decision.Verdict}/{decision.Reason}");
    }

    [Fact]
    public void Automatic_execution_allows_only_read_only_and_verified_reversible_actions()
    {
        var context = Ctx(ExecutionTrigger.Automatic, gaming: false, enabled: true, confirmed: false, canElevate: true);

        foreach (var level in Enum.GetValues<SafetyLevel>())
        foreach (var mode in Enum.GetValues<ActionMode>())
        foreach (var reversible in new[] { false, true })
        foreach (var verified in new[] { false, true })
        foreach (var confirm in new[] { false, true })
        {
            var action = D(level, reversible, verified, confirm: confirm);
            var decision = Policy.Evaluate(action, mode, context);
            if (decision.Verdict != PolicyVerdict.Allow)
                continue;

            var safeLevel = level == L0ReadOnly || (level == L1Reversible && reversible && verified);
            var permissiveMode = mode is ActionMode.Allowed or ActionMode.Optional;
            Assert.True(
                safeLevel && permissiveMode && !confirm,
                $"Allow для {level}/{mode}, reversible={reversible}, verified={verified}, confirm={confirm}");
        }
    }
}
