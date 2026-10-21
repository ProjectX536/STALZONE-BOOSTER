using Booster.Core.Abstractions;
using Booster.Core.Latency;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Latency;

public sealed class TraceRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    private readonly FakeWprRunner _wpr = new();
    private readonly InMemoryStateFileStore _state = new();
    private readonly FakeBootTimeSource _boot = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ListLogger<TraceRecorder> _logger = new();

    public TraceRecorderTests() => _time.SetLocalTimeZone(Moscow);

    private TraceRecorder Recorder() => new(_wpr, _state, _boot, _time, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Recording(DateTimeOffset? startedAt = null) =>
        _state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(startedAt ?? Now.AddMinutes(-5)));

    [Fact]
    public async Task Start_writes_marker_before_launch()
    {
        string? markerAtLaunch = null;
        _wpr.OnStart = () => markerAtLaunch = _state.Files.GetValueOrDefault(TraceRecorder.MarkerFileName);

        var result = await Recorder().StartAsync(Ct);

        Assert.Equal(WprOutcome.Done, result.Outcome);
        Assert.NotNull(markerAtLaunch);
        Assert.Equal(new TraceStatus(TraceState.Recording, Now), await Recorder().GetStatusAsync(Ct));
    }

    [Fact]
    public async Task Declined_start_removes_marker()
    {
        _wpr.Result = new WprResult(WprOutcome.Declined);

        var result = await Recorder().StartAsync(Ct);

        Assert.Equal(WprOutcome.Declined, result.Outcome);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
        Assert.Equal(TraceState.Idle, (await Recorder().GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task Failed_start_removes_marker()
    {
        _wpr.Result = new WprResult(WprOutcome.Failed, ExitCode: 5, Detail: "exit code 5");

        var result = await Recorder().StartAsync(Ct);

        Assert.Equal(5, result.ExitCode);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
    }

    [Fact]
    public async Task Start_while_recording_is_refused()
    {
        Recording();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Recorder().StartAsync(Ct));

        Assert.Empty(_wpr.Calls);
    }

    [Fact]
    public async Task Stop_done_removes_marker_and_returns_path()
    {
        Recording();

        var result = await Recorder().StopAsync(Ct);

        Assert.Equal(["stop trace-20261021-153000.etl"], _wpr.Calls);
        Assert.Equal(Path.Combine(FakeWprRunner.TracesFolder, "trace-20261021-153000.etl"), result.Path);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
    }

    [Fact]
    public async Task Stop_failed_keeps_marker()
    {
        Recording();
        _wpr.Result = new WprResult(WprOutcome.Failed, ExitCode: 1);

        await Recorder().StopAsync(Ct);

        Assert.True(_state.Contains(TraceRecorder.MarkerFileName));
        Assert.Equal(TraceState.Recording, (await Recorder().GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task Stop_declined_keeps_marker()
    {
        Recording();
        _wpr.Result = new WprResult(WprOutcome.Declined);

        await Recorder().StopAsync(Ct);

        Assert.True(_state.Contains(TraceRecorder.MarkerFileName));
    }

    [Fact]
    public async Task Cancel_failed_removes_marker_declined_keeps()
    {
        Recording();
        _wpr.Result = new WprResult(WprOutcome.Declined);
        await Recorder().CancelAsync(Ct);
        Assert.True(_state.Contains(TraceRecorder.MarkerFileName));

        _wpr.Result = new WprResult(WprOutcome.Failed, ExitCode: 1);
        await Recorder().CancelAsync(Ct);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
        Assert.Equal(["cancel", "cancel"], _wpr.Calls);
    }

    [Fact]
    public async Task Cancel_done_removes_marker()
    {
        Recording();

        await Recorder().CancelAsync(Ct);

        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
    }

    [Fact]
    public async Task Marker_older_than_boot_is_interrupted()
    {
        var startedAt = FakeBootTimeSource.DefaultBoot.AddMinutes(-1);
        Recording(startedAt);

        Assert.Equal(new TraceStatus(TraceState.Interrupted, startedAt), await Recorder().GetStatusAsync(Ct));
    }

    [Fact]
    public async Task Marker_newer_than_boot_is_recording()
    {
        var startedAt = FakeBootTimeSource.DefaultBoot.AddMinutes(1);
        Recording(startedAt);

        Assert.Equal(new TraceStatus(TraceState.Recording, startedAt), await Recorder().GetStatusAsync(Ct));
    }

    [Fact]
    public async Task Unknown_boot_time_keeps_recording()
    {
        var startedAt = FakeBootTimeSource.DefaultBoot.AddDays(-3);
        Recording(startedAt);
        _boot.Boot = Reading<DateTimeOffset>.Unknown("test-boot");

        Assert.Equal(new TraceStatus(TraceState.Recording, startedAt), await Recorder().GetStatusAsync(Ct));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("""{ "schemaVersion": 99, "startedAt": "2026-10-21T12:00:00+00:00" }""")]
    [InlineData("""{ "schemaVersion": 1 }""")]
    public async Task Unreadable_marker_is_treated_as_recording(string marker)
    {
        _state.Put(TraceRecorder.MarkerFileName, marker);

        Assert.Equal(new TraceStatus(TraceState.Recording, null), await Recorder().GetStatusAsync(Ct));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Dismiss_only_removes_interrupted_marker()
    {
        Recording(FakeBootTimeSource.DefaultBoot.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recorder().DismissAsync(Ct));
        Assert.True(_state.Contains(TraceRecorder.MarkerFileName));

        Recording(FakeBootTimeSource.DefaultBoot.AddMinutes(-1));
        await Recorder().DismissAsync(Ct);

        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
        Assert.Empty(_wpr.Calls);
    }

    [Fact]
    public async Task Unavailable_when_wpr_missing()
    {
        _wpr.IsAvailable = false;

        Assert.Equal(new TraceStatus(TraceState.Unavailable, null), await Recorder().GetStatusAsync(Ct));
        var result = await Recorder().StartAsync(Ct);

        Assert.Equal(WprOutcome.Failed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Empty(_wpr.Calls);
        Assert.Empty(_state.Files);
    }
}
