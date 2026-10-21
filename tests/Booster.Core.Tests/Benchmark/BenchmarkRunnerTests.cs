using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Benchmark;

public sealed class BenchmarkRunnerTests
{
    private const string Source = "test";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly BenchmarkSchedule Schedule = new(2, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
    private static readonly BenchmarkRequest Request = new("game.exe", null, Schedule, new BenchmarkNotes(null, null, null, null), null);

    private readonly FakeTimeProvider _time = new(SessionFactory.Start);
    private readonly FakeSensorProvider _sensors;
    private readonly FakeProcessActivitySource _activity = new();
    private readonly FakeFrameCaptureProvider _capture;
    private readonly FakeDisplays _displays;
    private readonly ReadOnlyPower _power = new();
    private readonly ListProgress _progress = new();

    public BenchmarkRunnerTests()
    {
        _sensors = new FakeSensorProvider(_time);
        _capture = new FakeFrameCaptureProvider(_time) { Result = FramesInWindows };
        _displays = new FakeDisplays(_time);
    }

    private BenchmarkRunner CreateRunner()
    {
        var config = new FakeConfiguration();
        var recorder = new BenchmarkRecorder(_sensors, _activity, new FakeSystemPressureSource(), config, _time,
            new ListLogger<BenchmarkRecorder>());
        return new BenchmarkRunner(_capture, recorder, _displays, _power, config, _time, new ListLogger<BenchmarkRunner>());
    }

    /// <summary>По 601 кадру на окно, равномерно: первый кадр окна отбрасывается, остаётся 600.</summary>
    private FrameCaptureResult FramesInWindows(long startTimestamp)
    {
        var frames = Schedule.Windows(startTimestamp, _time.TimestampFrequency)
            .SelectMany(w => Enumerable.Range(0, 601).Select(k => new FrameSample(w.StartQpc + k * ((w.EndQpc - w.StartQpc) / 601), 16.7, 8, 12, 0)))
            .ToArray();
        return new FrameCaptureResult(FrameCaptureStatus.Completed,
            new FrameCapture("game.exe", 42, frames, "MsBetweenPresents", 0, ["Application"], ["Hardware: Independent Flip"], @"C:\c.csv"),
            Reading<AdapterLuid>.Unknown(Source), null);
    }

    /// <summary>Двигает время по секунде, пока серия не закончится; <paramref name="onStep"/> — после каждого шага.</summary>
    private async Task<BenchmarkSeries> DriveAsync(Task<BenchmarkSeries> run, Action? onStep = null)
    {
        // Старт захвата идёт в пуле потоков: время не двигается, пока захват не стартовал или не ждёт UAC.
        for (var i = 0; i < 2000 && _capture.StartedAt is null && !run.IsCompleted && (_capture.ElevationWait == TimeSpan.Zero || _capture.Calls == 0); i++)
            await Task.Delay(1, Ct);
        for (var step = 0; step < 200 && !run.IsCompleted; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            onStep?.Invoke();
            await Task.Delay(2, Ct);
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Phases_reported_in_order_with_end_times()
    {
        await DriveAsync(CreateRunner().RunAsync(Request, _progress, Ct));

        var start = _capture.StartedAt!.Value;
        Assert.Equal(
            [
                new BenchmarkProgress(BenchmarkPhase.StartingCapture, 0, 2, null),
                new BenchmarkProgress(BenchmarkPhase.StartDelay, 0, 2, start.AddSeconds(3)),
                new BenchmarkProgress(BenchmarkPhase.Run, 0, 2, start.AddSeconds(13)),
                new BenchmarkProgress(BenchmarkPhase.Pause, 0, 2, start.AddSeconds(18)),
                new BenchmarkProgress(BenchmarkPhase.Run, 1, 2, start.AddSeconds(28)),
                new BenchmarkProgress(BenchmarkPhase.Processing, 1, 2, null),
            ],
            _progress.Items);
        Assert.Equal(Schedule.CaptureDuration, _capture.LastRequest!.Duration);
    }

    [Fact]
    public async Task Windows_are_anchored_at_capture_start_not_at_request()
    {
        _capture.ElevationWait = TimeSpan.FromSeconds(5);

        var series = await DriveAsync(CreateRunner().RunAsync(Request, _progress, Ct));

        Assert.True(_capture.StartedAt >= SessionFactory.Start.AddSeconds(5));
        Assert.Equal(_capture.StartedAt!.Value.AddSeconds(3), _progress.Items.Single(p => p.Phase == BenchmarkPhase.StartDelay).EndsAt);
        Assert.All(series.Runs, run => Assert.Equal(600, run.Frames.Frames));
        Assert.Equal(2, series.Summary!.ValidRuns);
    }

    [Fact]
    public async Task Declined_elevation_returns_status_without_runs_and_no_samples()
    {
        _capture.NotStartedResult = new FrameCaptureResult(FrameCaptureStatus.ElevationDeclined, null, Reading<AdapterLuid>.Unknown(), "declined");

        var series = await CreateRunner().RunAsync(Request, _progress, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(FrameCaptureStatus.ElevationDeclined, series.Status);
        Assert.Equal("declined", series.Detail);
        Assert.Empty(series.Runs);
        Assert.Equal(0, _sensors.Calls);
        Assert.Equal(0, _activity.Calls);
        Assert.Equal(0, _displays.Calls);
        Assert.Equal([BenchmarkPhase.StartingCapture], _progress.Items.Select(p => p.Phase));
    }

    [Fact]
    public async Task PresentMon_exiting_after_start_ends_series_without_waiting_for_schedule()
    {
        _capture.ExitedAfterStartResult = new FrameCaptureResult(FrameCaptureStatus.Failed, null, Reading<AdapterLuid>.Unknown(),
            "PresentMon exited with code 1");

        // Время не двигается: серия должна закончиться сама, а не отсчитывать прогоны впустую.
        var series = await CreateRunner().RunAsync(Request, _progress, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(FrameCaptureStatus.Failed, series.Status);
        Assert.Equal("PresentMon exited with code 1", series.Detail);
        Assert.DoesNotContain(_progress.Items, p => p.Phase == BenchmarkPhase.Run);
    }

    [Fact]
    public async Task Cancellation_during_run_returns_cancelled_series()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var series = await DriveAsync(CreateRunner().RunAsync(Request, _progress, cts.Token), () =>
        {
            if (_progress.Items.Any(p => p.Phase == BenchmarkPhase.Run))
                cts.Cancel();
        });

        Assert.Equal(FrameCaptureStatus.Cancelled, series.Status);
        Assert.Empty(series.Runs);
        Assert.DoesNotContain(_progress.Items, p => p.Phase == BenchmarkPhase.Processing);
    }

    [Fact]
    public async Task Series_with_target_started_during_delay_has_frames_and_partial_telemetry()
    {
        var running = new ProcessActivity(Reading<double>.Known(30, Source),
            Reading<TargetCpuUsage>.Known(new TargetCpuUsage("game.exe", 42, 20, 60, 30), Source), []);
        var notYet = new ProcessActivity(Reading<double>.Known(10, Source),
            Reading<TargetCpuUsage>.Unknown(Source, "target process is not running"), []);
        _activity.Activity = call => call <= 1 ? notYet : running;

        var series = await DriveAsync(CreateRunner().RunAsync(Request, _progress, Ct));

        Assert.Equal(FrameCaptureStatus.Completed, series.Status);
        Assert.True(series.HasValidRuns);
        Assert.Equal(60, series.Telemetry.BusiestThreadPercent.Value);
        Assert.True(_activity.Calls > 2);
        Assert.Equal(("game.exe", (int?)null), _activity.LastTarget);
    }

    [Fact]
    public async Task Display_and_power_read_once_at_first_run_start()
    {
        var series = await DriveAsync(CreateRunner().RunAsync(Request, _progress, Ct));

        Assert.Equal(1, _displays.Calls);
        Assert.True(_displays.CalledAt >= _capture.StartedAt!.Value.AddSeconds(3));
        Assert.Equal(1, _power.ActiveSchemeCalls);
        Assert.Equal(1, _power.PowerSourceCalls);
        Assert.Equal("Balanced", series.Conditions.PowerPlan.Value);
        Assert.Equal(PowerSource.Battery, series.Conditions.PowerSource);
        Assert.Equal(144, series.Conditions.Display.Value!.RefreshRateHz);
    }

    [Fact]
    public async Task Display_detection_failure_gives_unknown_display_not_failure()
    {
        _displays.Throw = true;

        var series = await DriveAsync(CreateRunner().RunAsync(Request, _progress, Ct));

        Assert.Equal(FrameCaptureStatus.Completed, series.Status);
        Assert.False(series.Conditions.Display.IsAvailable);
        Assert.True(series.HasValidRuns);
    }

    [Fact]
    public async Task Series_id_is_local_start_time_and_safe_game_name()
    {
        _capture.NotStartedResult = new FrameCaptureResult(FrameCaptureStatus.NotInstalled, null, Reading<AdapterLuid>.Unknown(), null);

        var series = await CreateRunner().RunAsync(Request with { ProcessName = "my:game?.EXE" }, null, Ct);

        Assert.Equal("20261019-120000-my_game_", series.Id);
    }

    [Fact]
    public async Task Request_without_exactly_one_target_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateRunner().RunAsync(Request with { ProcessId = 42 }, null, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateRunner().RunAsync(Request with { ProcessName = null }, null, Ct));
        Assert.Equal(0, _capture.Calls);
    }

    private sealed class ListProgress : IProgress<BenchmarkProgress>
    {
        private readonly List<BenchmarkProgress> _items = [];
        private readonly Lock _lock = new();

        public IReadOnlyList<BenchmarkProgress> Items
        {
            get
            {
                lock (_lock)
                    return _items.ToArray();
            }
        }

        public void Report(BenchmarkProgress value)
        {
            lock (_lock)
                _items.Add(value);
        }
    }

    private sealed class FakeDisplays(TimeProvider time) : IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>
    {
        private int _calls;

        public string SectionId => "displays";

        public bool Throw { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public DateTimeOffset? CalledAt { get; private set; }

        public Task<Reading<IReadOnlyList<DisplayInfo>>> DetectAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            CalledAt = time.GetLocalNow();
            if (Throw)
                throw new InvalidOperationException("display query failed");
            IReadOnlyList<DisplayInfo> displays =
            [
                new(Reading<string>.Known("Monitor", Source), Reading<int>.Known(2560, Source), Reading<int>.Known(1440, Source),
                    Reading<double>.Known(144, Source), Reading<bool>.Known(false, Source), Reading<bool>.Known(false, Source),
                    Reading<bool>.Known(true, Source), Reading<AdapterLuid>.Known(new AdapterLuid(1, 0), Source), true),
            ];
            return Task.FromResult(Reading<IReadOnlyList<DisplayInfo>>.Known(displays, Source));
        }
    }

    /// <summary>Только чтение: смена схемы в бенчмарке — ошибка теста.</summary>
    private sealed class ReadOnlyPower : IPowerPlanProvider
    {
        private static readonly PowerScheme Balanced = new(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced");

        public int ActiveSchemeCalls { get; private set; }

        public int PowerSourceCalls { get; private set; }

        public IReadOnlyList<PowerScheme> ListSchemes() => [Balanced];

        public Guid GetActiveScheme()
        {
            ActiveSchemeCalls++;
            return Balanced.Id;
        }

        public void SetActiveScheme(Guid schemeId) => throw new InvalidOperationException("Benchmark must not change the power plan.");

        public PowerSource GetPowerSource()
        {
            PowerSourceCalls++;
            return PowerSource.Battery;
        }
    }
}
