using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Engine;

/// <summary>
/// Запускает сборщик одной секции с таймаутом и превращает любой исход в <see cref="Reading{T}"/>:
/// ошибка, пустой результат или зависание сборщика дают <see cref="DataStatus.Failed"/> и запись
/// уровня Warning, а не исключение. Исключение — только отмена через внешний токен.
/// </summary>
public sealed partial class SectionRunner(TimeProvider time, ILogger<SectionRunner> logger)
{
    public async Task<Reading<T>> RunAsync<T>(string sectionId, Func<CancellationToken, Task<T>> collect,
        TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ct.ThrowIfCancellationRequested();

        using var collectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<T> task;
        try
        {
            task = collect(collectorCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed<T>(sectionId, ex);
        }

        T value;
        try
        {
            // WaitAsync отпускает ожидание по таймауту, даже если сборщик не смотрит на токен.
            value = await task.WaitAsync(timeout, time, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex) when (!IsFaultOf(task, ex))
        {
            await collectorCts.CancelAsync().ConfigureAwait(false);
            ObserveLater(task);
            var ms = (long)timeout.TotalMilliseconds;
            LogTimedOut(sectionId, ms);
            return Reading<T>.Failure(sectionId, $"timeout after {ms} ms");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ObserveLater(task);
            throw;
        }
        catch (Exception ex)
        {
            return Failed<T>(sectionId, ex);
        }

        if (value is null)
        {
            LogNoValue(sectionId);
            return Reading<T>.Failure(sectionId, "collector returned no value");
        }

        return Reading<T>.Known(value, sectionId);
    }

    private Reading<T> Failed<T>(string sectionId, Exception ex)
    {
        LogFailed(ex, sectionId);
        return Reading<T>.Failure(sectionId, $"{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>Собственный <see cref="TimeoutException"/> сборщика — его ошибка, а не таймаут секции.</summary>
    private static bool IsFaultOf(Task task, Exception ex) =>
        task.IsFaulted && task.Exception!.InnerExceptions.Contains(ex);

    /// <summary>Брошенный сборщиком позже результат никто не ждёт; исключение помечается наблюдённым.</summary>
    private static void ObserveLater(Task task) =>
        task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Section {SectionId} failed")]
    private partial void LogFailed(Exception ex, string sectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Section {SectionId} timed out after {TimeoutMs} ms")]
    private partial void LogTimedOut(string sectionId, long timeoutMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Section {SectionId} collector returned no value")]
    private partial void LogNoValue(string sectionId);
}
