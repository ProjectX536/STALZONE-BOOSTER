namespace Booster.Platform.Windows.Monitoring;

public sealed record ProcessLaunch(string FileName, IReadOnlyList<string> Arguments, bool Elevate)
{
    /// <summary>Вызывается сразу после старта процесса; при Elevate — после согласия в UAC.</summary>
    public Action? Started { get; init; }
}

/// <summary>Запуск внешнего инструмента (PresentMon) с ожиданием выхода; подменяется в тестах.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Возвращает код выхода.
    /// <para>Elevate = false: процесс без окна; отмена убивает дерево процессов и бросает <see cref="OperationCanceledException"/>.</para>
    /// <para>
    /// Elevate = true: ShellExecute с глаголом "runas", окно скрыто. Отказ в UAC — <see cref="System.ComponentModel.Win32Exception"/>
    /// с кодом 1223 (ERROR_CANCELLED). Отмена бросает <see cref="OperationCanceledException"/>, а процесс продолжает работу:
    /// у неповышенного Booster нет прав его остановить.
    /// </para>
    /// </summary>
    Task<int> RunAsync(ProcessLaunch launch, CancellationToken ct);
}
