namespace Booster.Core.Profiles;

/// <summary>
/// Процессы, которые Booster не закрывает никогда, даже если пользователь их выбрал. Список живёт в коде,
/// а не в конфигурации: пользовательский файл не должен снимать защиту.
/// </summary>
public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Сам Booster.
        "Booster.App",

        // Оболочка и сеанс Windows.
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "svchost", "sihost", "ctfmon", "fontdrvhost",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "ApplicationFrameHost", "LockApp",

        // Защита.
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "smartscreen",

        // Античиты.
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "vgtray", "FACEIT",
        // EA Javelin (Battlefield, Apex Legends на EA App).
        "EAAntiCheat.GameService",
        // BattlEye: 64-битная служба.
        "BEService_x64",
        // PunkBuster (Even Balance): служба и её клиентская часть.
        "PnkBstrA", "PnkBstrB",
        // Служба клиента FACEIT.
        "FACEITService",
    };

    /// <summary>Без учёта регистра, после <see cref="ProcessNames.Normalize"/>.</summary>
    public static bool IsProtected(string processName) =>
        ProcessNames.Normalize(processName) is { } name && Names.Contains(name);
}
