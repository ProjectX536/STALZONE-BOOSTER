namespace Booster.Core.Engine;

public enum ExecutionTrigger
{
    /// <summary>Действие запускает движок (например, при старте игрового профиля).</summary>
    Automatic,

    /// <summary>Пользователь сам нажал кнопку действия.</summary>
    UserInitiated,
}

/// <param name="UserEnabled">Пользователь включил optional-действие в профиле.</param>
/// <param name="UserConfirmed">Пользователь подтвердил именно это выполнение.</param>
/// <param name="CanElevate">Доступен способ выполнить действие с правами администратора.</param>
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
