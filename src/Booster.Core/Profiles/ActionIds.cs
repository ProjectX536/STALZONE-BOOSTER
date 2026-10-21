namespace Booster.Core.Profiles;

/// <summary>Идентификаторы действий: Этапа 2 — те же, что в <c>actions</c> профиля; обслуживания — только в журнале.</summary>
public static class ActionIds
{
    public const string PowerPlan = "power-plan";
    public const string GamePriority = "game-priority";
    public const string BackgroundApps = "user-selected-background-apps";
    public const string StartupItem = "startup-item";

    /// <summary>Отключение элемента автозагрузки для всех пользователей (HKLM): только в Booster с правами администратора.</summary>
    public const string MachineStartupItem = "machine-startup-item";
    public const string RegistryTweak = "registry-tweak";
}
