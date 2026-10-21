namespace Booster.Core.Abstractions;

/// <summary>
/// Ключи HKCU, где Проводник хранит включение и отключение элементов автозагрузки текущего пользователя
/// (то же, что переключатель в Диспетчере задач): сам элемент автозагрузки при этом не удаляется.
/// </summary>
public static class StartupApprovedKeys
{
    public const string Run = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string StartupFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
}
