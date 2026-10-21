namespace Booster.Core.Abstractions;

public enum CloseRequestResult
{
    /// <summary>Все процессы с этим именем вышли.</summary>
    Closed,

    /// <summary>Окна получили запрос, но процесс не вышел до таймаута.</summary>
    StillRunning,

    /// <summary>У процесса нет видимого окна верхнего уровня — закрыть штатно нечего.</summary>
    NoWindow,

    /// <summary>Процесса с этим именем уже нет.</summary>
    NotRunning,
}

/// <summary>Штатное закрытие приложений — как кнопкой «×».</summary>
public interface IAppCloser
{
    /// <summary>
    /// WM_CLOSE видимым окнам верхнего уровня без владельца у всех процессов с этим именем, кроме своего; затем ожидание
    /// выхода до <paramref name="timeout"/>. Принудительно ничего не завершается.
    /// </summary>
    Task<CloseRequestResult> RequestCloseAsync(string processName, TimeSpan timeout, CancellationToken ct);
}
