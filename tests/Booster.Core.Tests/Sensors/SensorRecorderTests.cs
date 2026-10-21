using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Sensors;

public sealed class SensorRecorderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2000); // FakeConfiguration: sensorIntervalMs

    private readonly FakeTimeProvider _time = new(SessionFactory.Start);
    private readonly FakeSensorProvider _sensors;
    private readonly ListLogger<SensorRecorder> _logger = new();

    public SensorRecorderTests() => _sensors = new FakeSensorProvider(_time);

    private SensorRecorder CreateRecorder() => new(_sensors, new FakeConfiguration(), _time, _logger);

    [Fact]
    public async Task Records_immediately_and_then_every_interval()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var recording = CreateRecorder().RecordAsync(stop.Token);
        await _sensors.WaitForCallsAsync(1);
        for (var call = 2; call <= 4; call++)
        {
            _time.Advance(Interval);
            await _sensors.WaitForCallsAsync(call);
        }

        await stop.CancelAsync();
        var samples = await recording;

        Assert.Equal(4, samples.Count);
        Assert.Equal(
            [SessionFactory.Start, SessionFactory.Start.AddSeconds(2), SessionFactory.Start.AddSeconds(4), SessionFactory.Start.AddSeconds(6)],
            samples.Select(s => s.TakenAt));
    }

    [Fact]
    public async Task No_sample_is_taken_before_the_interval_passes()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var recording = CreateRecorder().RecordAsync(stop.Token);
        await _sensors.WaitForCallsAsync(1);
        _time.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await stop.CancelAsync();

        Assert.Single(await recording);
        Assert.Equal(1, _sensors.Calls);
    }

    [Fact]
    public async Task Failed_read_skips_the_sample_and_continues()
    {
        _sensors.FailOnCall = 2;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var recording = CreateRecorder().RecordAsync(stop.Token);
        await _sensors.WaitForCallsAsync(1);
        _time.Advance(Interval);
        await _sensors.WaitForCallsAsync(2);
        _time.Advance(Interval);
        await _sensors.WaitForCallsAsync(3);
        await stop.CancelAsync();
        var samples = await recording;

        Assert.Equal([SessionFactory.Start, SessionFactory.Start.AddSeconds(4)], samples.Select(s => s.TakenAt));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Cancellation_returns_collected_samples()
    {
        _sensors.HangOnCall = 2;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var recording = CreateRecorder().RecordAsync(stop.Token);
        await _sensors.WaitForCallsAsync(1);
        _time.Advance(Interval);
        await _sensors.WaitForCallsAsync(2);
        await stop.CancelAsync();
        var samples = await recording;

        Assert.Equal(SessionFactory.Start, Assert.Single(samples).TakenAt);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }
}
