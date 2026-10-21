namespace Booster.Core.Abstractions;

/// <summary>Место автозагрузки: ключ Run или RunOnce реестра либо папка «Автозагрузка».</summary>
public enum StartupLocation
{
    UserRun,
    UserRunOnce,
    MachineRun,

    /// <summary>32-битное представление HKLM (WOW6432Node).</summary>
    MachineRun32,
    MachineRunOnce,
    UserFolder,
    CommonFolder,
}

/// <summary>Состояние по ключам StartupApproved — тот же переключатель, что в Диспетчере задач.</summary>
public enum StartupState
{
    Enabled,
    Disabled,
    Unknown,
}

/// <param name="Name">Имя значения Run или имя файла в папке — оно же имя значения в StartupApproved.</param>
/// <param name="Command">Команда из реестра или путь к файлу в папке.</param>
/// <param name="ExecutablePath">null — не определён (ярлык без цели, команда без exe).</param>
/// <param name="State">RunOnce — всегда Enabled: StartupApproved к ним не относится.</param>
public sealed record StartupEntry(string Name, StartupLocation Location, string Command, string? ExecutablePath, StartupState State);

/// <summary>Элементы автозагрузки текущего пользователя и машины. Только чтение.</summary>
public interface IStartupSource
{
    /// <summary>Ошибка одного места не мешает остальным; исключение — ни одно место не прочитано.</summary>
    IReadOnlyList<StartupEntry> Read();
}
