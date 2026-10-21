namespace Booster.Core.Engine;

public enum ExecutionTrigger
{
    Automatic,

    UserInitiated,
}

public sealed record PolicyContext(
    ExecutionTrigger Trigger,
    bool IsGamingSessionActive,
    bool UserEnabled,
    bool UserConfirmed,
    bool CanElevate);

public enum PolicyVerdict
{
    Allow,
    RequireConfirmation,
    RecommendOnly,
    NotEnabled,
    Deny,
}

public enum PolicyReason
{
    Allowed,
    UnknownAction,
    Forbidden,
    ManualOnlyLevel,
    RecommendOnlyMode,
    NotDuringGaming,
    RequiresAdmin,
    OptionalNotEnabled,
    ConfirmationRequired,
    Irreversible,
    NotVerifiedForAutomatic,
}

public sealed record PolicyDecision(PolicyVerdict Verdict, PolicyReason Reason);
