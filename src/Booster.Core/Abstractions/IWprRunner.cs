namespace Booster.Core.Abstractions;

public enum WprOutcome
{
    Done,

    /// <summary>Пользователь отказал в запросе UAC.</summary>
    Declined,

    Failed,
}

/// <param name="ExitCode">Код выхода wpr.exe при Failed; null — процесс не запустился.</param>
/// <param name="Detail">Причина сбоя для журнала и пользователя.</param>
/// <param name="Path">Полный путь .etl после успешной остановки.</param>
public sealed record WprResult(WprOutcome Outcome, int? ExitCode = null, string? Detail = null, string? Path = null);

/// <summary>
/// Windows Performance Recorder из системного каталога: каждая операция — отдельный запуск wpr.exe через UAC
/// с фиксированными аргументами. Аргументы не берутся из конфигурации: она не должна задавать повышенный процесс.
/// </summary>
public interface IWprRunner
{
    /// <summary>wpr.exe есть в системном каталоге.</summary>
    bool IsAvailable { get; }

    Task<WprResult> StartAsync(CancellationToken ct);

    /// <param name="fileName">Только имя *.etl; каталог — traces.</param>
    /// <exception cref="ArgumentException">Имя с путём, «..» или не .etl.</exception>
    Task<WprResult> StopAsync(string fileName, CancellationToken ct);

    Task<WprResult> CancelAsync(CancellationToken ct);
}
