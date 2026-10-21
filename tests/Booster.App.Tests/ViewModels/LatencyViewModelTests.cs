using System.Globalization;
using Booster.App.Resources;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Latency;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.ViewModels;

public sealed class LatencyViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 30, 0, TimeSpan.Zero);

    private static readonly string TracePath = Path.Combine(FakeWprRunner.TracesFolder, "trace-20261021-123000.etl");

    private readonly FakeWprRunner _wpr = new();
    private readonly InMemoryStateFileStore _state = new();
    private readonly FakeSettingsOpener _opener = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("g", Culture);

    private LatencyViewModel Latency() => new(new TraceRecorder(_wpr, _state, new FakeBootTimeSource(), new FakeTimeProvider(Now),
        NullLogger<TraceRecorder>.Instance), _opener, NullLogger<LatencyViewModel>.Instance);

    private async Task<LatencyViewModel> LoadedAsync()
    {
        var latency = Latency();
        await latency.RefreshAsync(Ct);
        return latency;
    }

    [Fact]
    public void Report_advice_names_the_screen_panel_and_button_as_shown()
    {
        string[] texts =
        [
            ReportStrings.RecommendationId_RecordLatencyTrace, ReportStrings.AdviceAction_DpcTimeHigh, ReportStrings.AdviceAction_InterruptTimeHigh,
        ];

        Assert.All(texts, text => Assert.Contains($"«{Strings.NavSystem} → {Strings.LatencyTitle} → {Strings.LatencyStart}»", text,
            StringComparison.Ordinal));
    }

    private static void AssertOffered(LatencyViewModel latency, bool start = false, bool stop = false, bool dismiss = false)
    {
        Assert.Equal(start, latency.StartTraceCommand.CanExecute(null));
        Assert.Equal(stop, latency.StopTraceCommand.CanExecute(null));
        Assert.Equal(stop, latency.CancelTraceCommand.CanExecute(null));
        Assert.Equal(dismiss, latency.DismissTraceCommand.CanExecute(null));
    }

    [Fact]
    public void Nothing_is_offered_before_the_state_is_read()
    {
        var latency = Latency();

        Assert.True(latency.IsBusy);
        AssertOffered(latency);
    }

    [Fact]
    public async Task Idle_offers_start_only()
    {
        var latency = await LoadedAsync();

        Assert.Equal(TraceState.Idle, latency.State);
        Assert.False(latency.IsBusy);
        Assert.Null(latency.Banner);
        AssertOffered(latency, start: true);
        Assert.False(latency.RevealTraceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_done_shows_recording_banner()
    {
        var latency = await LoadedAsync();

        await latency.StartTraceCommand.ExecuteAsync(null);

        Assert.Equal(["start"], _wpr.Calls);
        Assert.Equal(TraceState.Recording, latency.State);
        Assert.Equal(string.Format(Culture, Strings.LatencyRecordingFormat, Time(Now)), latency.Banner);
        Assert.Contains("остановите сразу после рывка: WPR хранит только последний отрезок", latency.Banner, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Strings.LatencyStarted, latency.Status);
        AssertOffered(latency, stop: true);
    }

    [Fact]
    public async Task Declined_start_writes_nothing()
    {
        var latency = await LoadedAsync();
        _wpr.Result = new WprResult(WprOutcome.Declined);

        await latency.StartTraceCommand.ExecuteAsync(null);

        Assert.Equal(TraceState.Idle, latency.State);
        Assert.Null(latency.Banner);
        Assert.Equal(Strings.LatencyDeclined, latency.Status);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
        AssertOffered(latency, start: true);
    }

    [Fact]
    public async Task Declined_stop_keeps_banner()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);
        var banner = latency.Banner;
        _wpr.Result = new WprResult(WprOutcome.Declined);

        await latency.StopTraceCommand.ExecuteAsync(null);

        Assert.Equal(TraceState.Recording, latency.State);
        Assert.NotNull(banner);
        Assert.Equal(banner, latency.Banner);
        Assert.Equal(Strings.LatencyDeclined, latency.Status);
        Assert.Null(latency.LastTracePath);
        AssertOffered(latency, stop: true);
    }

    [Fact]
    public async Task Done_stop_offers_reveal()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);

        await latency.StopTraceCommand.ExecuteAsync(null);

        Assert.Equal(["start", "stop trace-20261021-123000.etl"], _wpr.Calls);
        Assert.Equal(TraceState.Idle, latency.State);
        Assert.Null(latency.Banner);
        Assert.Equal(TracePath, latency.LastTracePath);
        Assert.Equal(string.Format(Culture, Strings.LatencySavedFormat, TracePath), latency.Status);
        Assert.True(latency.RevealTraceCommand.CanExecute(null));

        latency.RevealTraceCommand.Execute(null);

        Assert.Equal([TracePath], _opener.Revealed);
    }

    [Fact]
    public async Task Reveal_failure_is_reported()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);
        await latency.StopTraceCommand.ExecuteAsync(null);
        _opener.RevealResult = false;

        latency.RevealTraceCommand.Execute(null);

        Assert.Equal(Strings.LatencyRevealFailed, latency.Status);
    }

    [Fact]
    public async Task Done_cancel_removes_banner()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);

        await latency.CancelTraceCommand.ExecuteAsync(null);

        Assert.Equal(["start", "cancel"], _wpr.Calls);
        Assert.Equal(TraceState.Idle, latency.State);
        Assert.Null(latency.Banner);
        Assert.Equal(Strings.LatencyCancelled, latency.Status);
        Assert.Null(latency.LastTracePath);
    }

    [Fact]
    public async Task Failed_stop_shows_exit_code_and_keeps_banner()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);
        _wpr.Result = new WprResult(WprOutcome.Failed, ExitCode: unchecked((int)0xC5583001));

        await latency.StopTraceCommand.ExecuteAsync(null);

        Assert.Equal(TraceState.Recording, latency.State);
        Assert.NotNull(latency.Banner);
        Assert.Equal(string.Format(Culture, Strings.LatencyFailedFormat, string.Format(Culture, Strings.LatencyExitCodeFormat, "C5583001")),
            latency.Status);
        AssertOffered(latency, stop: true);
    }

    [Fact]
    public async Task Unreadable_marker_offers_stop_and_cancel()
    {
        _state.Put(TraceRecorder.MarkerFileName, "{ not json");

        var latency = await LoadedAsync();

        Assert.Equal(TraceState.Recording, latency.State);
        Assert.Equal(Strings.LatencyRecordingUnknown, latency.Banner);
        AssertOffered(latency, stop: true);
    }

    [Fact]
    public async Task Interrupted_offers_dismiss_without_uac()
    {
        var startedAt = FakeBootTimeSource.DefaultBoot.AddMinutes(-10);
        _state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(startedAt));
        var latency = await LoadedAsync();

        Assert.Equal(TraceState.Interrupted, latency.State);
        Assert.Equal(string.Format(Culture, Strings.LatencyInterruptedFormat, Time(startedAt)), latency.Banner);
        AssertOffered(latency, dismiss: true);

        await latency.DismissTraceCommand.ExecuteAsync(null);

        Assert.Empty(_wpr.Calls);
        Assert.False(_state.Contains(TraceRecorder.MarkerFileName));
        Assert.Equal(TraceState.Idle, latency.State);
        Assert.Null(latency.Banner);
        Assert.Equal(Strings.LatencyDismissed, latency.Status);
        AssertOffered(latency, start: true);
    }

    [Fact]
    public async Task Unavailable_disables_start()
    {
        _wpr.IsAvailable = false;

        var latency = await LoadedAsync();

        Assert.Equal(TraceState.Unavailable, latency.State);
        Assert.Contains("wpr.exe не найден в System32", latency.Status, StringComparison.Ordinal);
        Assert.Null(latency.Banner);
        AssertOffered(latency);
    }

    [Fact]
    public async Task Panel_with_banner_loads()
    {
        var latency = await LoadedAsync();
        await latency.StartTraceCommand.ExecuteAsync(null);

        Assert.Null(WpfHost.TryRender(() => new Booster.App.Views.LatencyPanel { DataContext = latency }));
    }
}
