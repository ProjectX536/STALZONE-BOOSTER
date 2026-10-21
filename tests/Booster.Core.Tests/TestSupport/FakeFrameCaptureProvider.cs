using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Tests.TestSupport;

/// <summary>
/// Захват без PresentMon: ждёт «ответа UAC», вызывает started, ждёт длительность захвата по времени <paramref name="time"/>
/// и отдаёт результат, построенный от метки старта. Отмена — Cancelled, как у настоящего провайдера.
/// </summary>
public sealed class FakeFrameCaptureProvider(TimeProvider time) : IFrameCaptureProvider
{
    private int _calls;

    /// <summary>Сколько «ждать согласия в UAC» до старта.</summary>
    public TimeSpan ElevationWait { get; set; }

    /// <summary>Не null — PresentMon не стартует: started не вызывается, сразу возвращается этот результат.</summary>
    public FrameCaptureResult? NotStartedResult { get; set; }

    /// <summary>Не null — PresentMon стартовал и сразу завершился (ошибка ETW, сбой): started вызван, возвращается этот результат.</summary>
    public FrameCaptureResult? ExitedAfterStartResult { get; set; }

    /// <summary>Результат по метке QPC старта.</summary>
    public Func<long, FrameCaptureResult> Result { get; set; } = _ => Empty;

    public static readonly FrameCaptureResult Empty = new(FrameCaptureStatus.NoFrames, null, Reading<AdapterLuid>.Unknown(), null);

    public FrameCaptureRequest? LastRequest { get; private set; }

    /// <summary>Время старта (после вызова started); null — не стартовал.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    public int Calls => Volatile.Read(ref _calls);

    public FrameCaptureAvailability Availability { get; set; } = new(true, @"C:\PresentMon.exe", "2.3.0", FrameCaptureMode.Direct);

    /// <summary>Не null — CaptureAsync бросает это исключение.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Захват завершился отменой токена.</summary>
    public bool WasCancelled { get; private set; }

    public Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(Availability);

    public async Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastRequest = request;
        if (Failure is { } failure)
            throw failure;
        try
        {
            if (ElevationWait > TimeSpan.Zero)
                await Task.Delay(ElevationWait, time, cancellationToken).ConfigureAwait(false);
            if (NotStartedResult is { } notStarted)
                return notStarted;

            var timestamp = time.GetTimestamp();
            started?.Invoke();
            StartedAt = time.GetLocalNow();
            if (ExitedAfterStartResult is { } exited)
                return exited;
            await Task.Delay(request.Duration, time, cancellationToken).ConfigureAwait(false);
            return Result(timestamp);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            return new FrameCaptureResult(FrameCaptureStatus.Cancelled, null, Reading<AdapterLuid>.Unknown(), null);
        }
    }
}
