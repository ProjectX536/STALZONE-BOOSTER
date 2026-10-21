namespace Booster.Core.Configuration;

/// <summary>
/// Содержимое <c>system-catalog.json</c> — справочники политики безопасности. Читается только встроенный файл:
/// пользовательский не должен снимать защиту со служб.
/// </summary>
/// <param name="Overlays">Программы с внутриигровым оверлеем; запущенный процесс не доказывает, что оверлей включён.</param>
/// <param name="RegistryTweaks">Белый список настроек HKCU, которые Booster может изменить с подтверждением.</param>
public sealed record SystemCatalog(int SchemaVersion, ServiceCatalog Services, IReadOnlyList<OverlayDefinition> Overlays,
    IReadOnlyList<RegistryTweakDefinition> RegistryTweaks)
{
    public const int CurrentSchemaVersion = 1;
}

/// <param name="Process">Имя процесса без «.exe»; сравнивается без учёта регистра.</param>
/// <param name="Name">Название программы для пользователя.</param>
public sealed record OverlayDefinition(string Process, string Name);

/// <summary>Одна настройка белого списка реестра. Предлагается только по правилу-доказательству.</summary>
/// <param name="Id">Одна из <see cref="RegistryTweakIds"/>; записи с другим Id не используются.</param>
/// <param name="SubKey">Путь относительно HKCU.</param>
/// <param name="ValueName">null — имя значения задаёт правило: путь к exe игры.</param>
/// <param name="Value">Для DWORD — десятичное число; для пары в строке UserGpuPreferences — «Ключ=Значение» без «;».</param>
/// <param name="MinBuild">Ниже этой сборки Windows настройка только рекомендуется.</param>
public sealed record RegistryTweakDefinition(string Id, string SubKey, string? ValueName, string Value, int MinBuild);

/// <summary>Идентификаторы настроек белого списка: правила-доказательства привязаны к ним.</summary>
public static class RegistryTweakIds
{
    public const string GameMode = "game-mode";
    public const string GpuHighPerformance = "gpu-high-performance";
    public const string WindowedFlipModel = "windowed-flip-model";
}

/// <summary>
/// Службы, которые нельзя предлагать к проверке. Имена — короткие имена служб, сравниваются без учёта регистра;
/// ключевые слова ищутся в имени, отображаемом имени и издателе (в нижнем регистре).
/// </summary>
public sealed record ServiceCatalog(
    IReadOnlyList<string> SystemCritical,
    IReadOnlyList<string> Security,
    IReadOnlyList<string> SecurityKeywords,
    IReadOnlyList<string> AntiCheat,
    IReadOnlyList<string> AntiCheatKeywords,
    IReadOnlyList<string> DeviceKeywords);
