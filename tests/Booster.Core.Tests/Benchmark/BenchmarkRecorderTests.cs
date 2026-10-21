using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Benchmark;

public sealed class BenchmarkRecorderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2000);

    private readonly FakeTimeProvider _time = new(SessionFactory.Start);
    private readonly FakeSensorProvider _sensors;
    private readonly FakeProcessActivitySource _activity = new();
    private readonly FakeSystemPressureSource _pressure = new();

    public BenchmarkRecorderTests() => _sensors = new FakeSensorProvider(_time);

    private BenchmarkRecorder CreateRecorder() =>
        new(_sensors, _activity, _pressure, new FakeConfiguration(), _time, new ListLogger<BenchmarkRecorder>());

    private async Task<IReadOnlyList<BenchmarkSample>> RecordTicksAsync(int samples, string? processName, int? processId)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var recording = CreateRecorder().RecordAsync(processName, processId, stop.Token);
        await _sensors.WaitForCallsAsync(1);
        for (var call = 2; call <= samples; call++)
        {
            _time.Advance(Interval);
            await _sensors.WaitForCallsAsync(call);
        }
        await stop.CancelAsync();
        return await recording;
    }

    [Fact]
    public async Task Records_until_stopped_at_sensor_interval()
    {
        var samples = await RecordTicksAsync(4, "game.exe", null);

        Assert.Equal(4, samples.Count);
        Assert.Equal(
            [SessionFactory.Start, SessionFactory.Start.AddSeconds(2), SessionFactory.Start.AddSeconds(4), SessionFactory.Start.AddSeconds(6)],
            samples.Select(s => s.At));
        Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.Second.Timestamp > pair.First.Timestamp));
        Assert.All(samples, s => Assert.NotNull(s.Sensors));
        Assert.All(samples, s => Assert.NotNull(s.Activity));
        Assert.All(samples, s => Assert.NotNull(s.Pressure));
        Assert.Equal(("game.exe", (int?)null), _activity.LastTarget);
    }

    [Fact]
    public async Task Failing_source_gives_null_part_and_keeps_recording()
    {
        _activity.FailOnCall = 2;
        _pressure.FailOnCall = 3;
        _sensors.FailOnCall = 3;

        var samples = await RecordTicksAsync(4, null, 4242);

        Assert.Equal(4, samples.Count);
        Assert.Equal([true, false, true, true], samples.Select(s => s.Activity is not null));
        Assert.Equal([true, true, false, true], samples.Select(s => s.Pressure is not null));
        Assert.Equal([true, true, false, true], samples.Select(s => s.Sensors is not null));
        Assert.Equal((null, 4242), _activity.LastTarget);
    }

    [Fact]
    public async Task Stop_before_first_interval_gives_one_sample()
    {
        var samples = await RecordTicksAsync(1, "game.exe", null);

        Assert.Single(samples);
        Assert.Equal(1, _sensors.Calls);
    }
}
