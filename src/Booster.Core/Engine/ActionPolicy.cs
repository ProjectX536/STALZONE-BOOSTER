using Booster.Core.Abstractions;

namespace Booster.Core.Engine;

public sealed class ActionPolicy : IActionPolicy
{
    public PolicyDecision Evaluate(ActionDescriptor action, ActionMode? configuredMode, PolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);

        if (configuredMode is not { } mode)
            return new(PolicyVerdict.RecommendOnly, PolicyReason.UnknownAction);
        if (mode == ActionMode.Forbidden)
            return new(PolicyVerdict.Deny, PolicyReason.Forbidden);
        if (action.Level == SafetyLevel.L3ManualOnly)
            return new(PolicyVerdict.RecommendOnly, PolicyReason.ManualOnlyLevel);
        if (mode == ActionMode.RecommendOnly)
            return new(PolicyVerdict.RecommendOnly, PolicyReason.RecommendOnlyMode);
        if (context.IsGamingSessionActive && (action.RequiresReboot || !action.AllowedDuringGaming))
            return new(PolicyVerdict.Deny, PolicyReason.NotDuringGaming);
        if (action.RequiresAdmin && !context.CanElevate)
            return new(PolicyVerdict.Deny, PolicyReason.RequiresAdmin);
        if (mode == ActionMode.Optional && !context.UserEnabled)
            return new(PolicyVerdict.NotEnabled, PolicyReason.OptionalNotEnabled);

        var confirmationReason = ConfirmationReason(action, mode, context.Trigger);
        if (confirmationReason is null || context.UserConfirmed)
            return new(PolicyVerdict.Allow, PolicyReason.Allowed);

        return new(PolicyVerdict.RequireConfirmation, confirmationReason.Value);
    }

    private static PolicyReason? ConfirmationReason(ActionDescriptor action, ActionMode mode, ExecutionTrigger trigger)
    {
        if (mode == ActionMode.AskFirst || action.Level == SafetyLevel.L2Confirmed || action.RequiresConfirmation)
            return PolicyReason.ConfirmationRequired;
        if (!action.IsReversible)
            return PolicyReason.Irreversible;
        if (trigger == ExecutionTrigger.Automatic && action.Level == SafetyLevel.L1Reversible && !action.VerifiedForAutomatic)
            return PolicyReason.NotVerifiedForAutomatic;
        return null;
    }
}
