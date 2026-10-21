using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.TestSupport;

/// <summary>
/// Датчики, которые отмечают каждый замер временем из <paramref name="time"/>; замер с номером <see cref="FailOnCall"/>
/// бросает, с номером <see cref="HangOnCall"/> — ждёт отмены.
/// </summary>
public sealed class FakeSensorProvider(TimeProvider time) : ISensorProvider
{
    private readonly SemaphoreSlim _calls = new(0);
    private int _count;

    public int FailOnCall { get; set; }

    /// <summary>true — каждый замер бросает.</summary>
    public bool FailAlways { get; set; }

    /// <summary>Замер в момент времени; по умолчанию — без адаптеров.</summary>
    public Func<DateTimeOffset, SensorSnapshot>? Sample { get; set; }

    public int HangOnCall { get; set; }

    /// <summary>
    /// Замер с этим номером блокирует вызывающий поток до отмены, ещё до возврата задачи, — как зависший нативный вызов.
    /// Предохранитель — <see cref="BlockLimit"/>, чтобы сломанный код не держал поток вечно.
    /// </summary>
    public int BlockOnCall { get; set; }

    public static readonly TimeSpan BlockLimit = TimeSpan.FromSeconds(30);

    /// <summary>true — блокировка дошла до <see cref="BlockLimit"/>: замер никто не отменил.</summary>
    public bool BlockRanOut { get; private set; }

    public int Calls => Volatile.Read(ref _count);

    public Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct) =>
        throw new NotSupportedException();

    public async Task<SensorSnapshot> ReadAsync(CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _count);
        _calls.Release();
        if (call == BlockOnCall && !ct.WaitHandle.WaitOne(BlockLimit))
            BlockRanOut = true;
        if (call == HangOnCall)
            await Task.Delay(Timeout.Infinite, ct);
        if (call == FailOnCall || FailAlways)
            throw new InvalidOperationException("sensor read failed");
        return Sample?.Invoke(time.GetUtcNow()) ?? new SensorSnapshot(time.GetUtcNow(), [], SensorSamples.Cpu());
    }

    public async Task WaitForCallsAsync(int count)
    {
        while (Calls < count)
            Assert.True(await _calls.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), $"sensor read {count} did not happen");
        Assert.Equal(count, Calls);
    }
}
