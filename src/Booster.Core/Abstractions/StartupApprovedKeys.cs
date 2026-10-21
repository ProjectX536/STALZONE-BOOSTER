namespace Booster.Core.Abstractions;

/// <summary>
/// Ключи, где Проводник хранит включение и отключение элементов автозагрузки (то же, что переключатель в Диспетчере задач): сам
/// элемент автозагрузки при этом не удаляется. Run и StartupFolder есть в HKCU и в HKLM по одному пути, Run32 — только в HKLM.
/// </summary>
public static class StartupApprovedKeys
{
    public const string Run = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string StartupFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>HKLM: состояние 32-битного ключа Run (WOW6432Node).</summary>
    public const string Run32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
}
