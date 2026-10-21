using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeSensorProvider(TimeProvider time) : ISensorProvider
{
    private readonly SemaphoreSlim _calls = new(0);
    private int _count;

    public int FailOnCall { get; set; }

    public bool FailAlways { get; set; }

    public Func<DateTimeOffset, SensorSnapshot>? Sample { get; set; }

    public int HangOnCall { get; set; }

    public int BlockOnCall { get; set; }

    public static readonly TimeSpan BlockLimit = TimeSpan.FromSeconds(30);

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
