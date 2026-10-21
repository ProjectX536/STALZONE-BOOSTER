namespace Booster.Core.Profiles;

// Список в коде, а не в конфиге: пользовательский файл не должен снимать защиту.
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
        "EAAntiCheat.GameService",
        "BEService_x64",
        "PnkBstrA", "PnkBstrB",
        "FACEITService",
    };

    public static bool IsProtected(string processName) =>
        ProcessNames.Normalize(processName) is { } name && Names.Contains(name);
}
