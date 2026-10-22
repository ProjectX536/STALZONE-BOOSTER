using System.Globalization;
using Booster.App.Resources;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Providers.GameNetwork;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class NetworkViewModelTests : IDisposable
{
    private readonly ScreenSession _screen = new();
    private readonly InMemoryGameNetworkStore _store = new();
    private readonly FakeGameNetworkMeasurer _measurer;
    private readonly FakeLoadTestRunner _load = new();
    private readonly FakeSettingsOpener _settings = new();

    public NetworkViewModelTests() => _measurer = new FakeGameNetworkMeasurer(_store);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NetworkViewModel Network(IGameNetworkStore? store = null) => new(_measurer, _load, store ?? _store, _screen.Session, _settings,
        _screen.Config, NullLogger<NetworkViewModel>.Instance);

    private static string Ms(double value) =>
        string.Format(CultureInfo.CurrentCulture, ReportStrings.UnitMs, value.ToString("0.#", CultureInfo.CurrentCulture));

    private static GameNetworkMeasurement Run(string id, string? label, int minutes) =>
        GameNetworkSamples.Measurement(id, label) with { StartedAt = GameNetworkSamples.StartedAt.AddMinutes(minutes) };

    private async Task<NetworkViewModel> MeasuredAsync(GameNetworkMeasurement? measurement = null)
    {
        _measurer.Saves(measurement ?? GameNetworkSamples.Measurement());
        var network = Network();
        await network.MeasureCommand.ExecuteAsync(null);
        return network;
    }

    [Fact]
    public async Task Measure_shows_breakdown_and_saves()
    {
        var network = await MeasuredAsync();

        Assert.Equal([ReportStrings.GameNetworkHome, ReportStrings.GameNetworkLine, ReportStrings.GameNetworkRoute, ReportStrings.GameNetworkTotal],
            network.BreakdownRows.Select(r => r.Label));
        Assert.Equal([Ms(2), Ms(10), Ms(28), Ms(40)], network.BreakdownRows.Select(r => r.Value.Text));
        Assert.Single(network.Measurements);
        Assert.NotNull(_screen.Session.GameNetwork);
        Assert.Contains(GameNetworkSamples.Server, network.ServerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_game_shows_hint()
    {
        _measurer.Run = _ => Task.FromResult(new GameNetworkRunResult(GameNetworkRunStatus.NoGame, null, null));
        var network = Network();

        await network.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(Strings.NetworkNoGame, network.StatusText);
        Assert.Empty(network.Measurements);
        Assert.Null(_screen.Session.GameNetwork);
    }

    [Fact]
    public async Task Invalid_manual_server_shows_error()
    {
        _measurer.Run = _ => Task.FromResult(new GameNetworkRunResult(GameNetworkRunStatus.InvalidManualServer, null, null));
        var network = Network();
        network.ManualServer = "10.0.0.1";

        await network.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(Strings.NetworkInvalidServer, network.StatusText);
        Assert.Equal("10.0.0.1", Assert.Single(_measurer.Requests).ManualServer);
    }

    [Fact]
    public async Task Use_candidate_fills_manual_server()
    {
        var network = await MeasuredAsync();

        network.UseCandidateCommand.Execute("198.51.100.20");

        Assert.Equal("198.51.100.20", network.ManualServer);
        Assert.Single(_measurer.Requests);
    }

    [Fact]
    public async Task Cancel_stops_measurement_without_saving()
    {
        _measurer.Run = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new GameNetworkRunResult(GameNetworkRunStatus.NoGame, null, null);
        };
        var network = Network();

        var run = network.MeasureCommand.ExecuteAsync(null);
        Assert.True(network.IsMeasuring);
        Assert.False(network.MeasureCommand.CanExecute(null));
        network.CancelCommand.Execute(null);
        await run;

        Assert.Equal(Strings.NetworkMeasureCancelled, network.StatusText);
        Assert.False(network.IsMeasuring);
        Assert.Empty(network.Measurements);
        Assert.Null(_screen.Session.GameNetwork);
    }

    [Fact]
    public async Task Measure_error_shows_failure_and_keeps_list()
    {
        _measurer.Run = _ => Task.FromException<GameNetworkRunResult>(new IOException("disk full"));
        var network = Network();

        await network.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(Strings.NetworkMeasureFailed, network.StatusText);
        Assert.Empty(network.Measurements);
    }

    [Fact]
    public async Task Game_exit_says_measurement_is_short()
    {
        var network = await MeasuredAsync(GameNetworkSamples.Measurement(end: MeasurementEnd.GameExited));

        Assert.Equal(Strings.NetworkGameExited, network.StatusText);
    }

    [Fact]
    public async Task Location_denied_shows_privacy_hint()
    {
        var denied = Reading<WifiStatus>.Known(new WifiStatus(
            Reading<int>.Unknown(GameNetworkMeasurer.WifiSource, WifiSource.AccessDeniedNote),
            Reading<int>.Known(36, GameNetworkMeasurer.WifiSource),
            Reading<WifiBand>.Unknown(GameNetworkMeasurer.WifiSource, WifiSource.AccessDeniedNote),
            Reading<bool>.Known(true, GameNetworkMeasurer.WifiSource),
            Reading<bool>.Known(false, GameNetworkMeasurer.WifiSource)), GameNetworkMeasurer.WifiSource);
        var network = await MeasuredAsync(GameNetworkSamples.Measurement(wifi: denied));

        network.OpenSettingsCommand.Execute("privacy-location");

        Assert.True(network.ShowLocationHint);
        Assert.Equal(["privacy-location"], _settings.Pages);
    }

    [Fact]
    public async Task Wifi_rows_include_link_speed_and_cable_has_text()
    {
        var wireless = await MeasuredAsync();
        _measurer.Saves(GameNetworkSamples.Measurement(adapter: FakeNetworkAdapters.Adapter("eth0", NetworkMedium.Ethernet, 1_000_000_000, GameNetworkSamples.Gateway),
            wifi: Reading<WifiStatus>.Unsupported(GameNetworkMeasurer.WifiSource, "cable")));
        var cable = Network();
        await cable.MeasureCommand.ExecuteAsync(null);

        Assert.Contains(wireless.WifiRows, r => r.Label == ReportStrings.GameNetworkLinkSpeed);
        Assert.False(wireless.ShowLocationHint);
        Assert.Empty(cable.WifiRows);
        Assert.Equal(ReportStrings.GameNetworkCable, cable.WifiText);
    }

    [Fact]
    public async Task Load_test_waits_for_continue()
    {
        var network = await MeasuredAsync();
        var states = new List<LoadTestState>();
        network.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NetworkViewModel.LoadState))
                lock (states)
                    states.Add(network.LoadState);
        };

        var run = network.StartLoadTestCommand.ExecuteAsync(null);
        Assert.Equal(LoadTestState.Idling, network.LoadState);
        Assert.False(network.MeasureCommand.CanExecute(null));
        _load.IdleDone.SetResult();
        await ScreenSession.WaitUntilAsync(() => network.LoadState == LoadTestState.WaitingForLoad, "waiting for load");
        Assert.Equal(Strings.NetworkLoadHint, network.LoadText);
        network.ContinueLoadCommand.Execute(null);
        await ScreenSession.WaitUntilAsync(() => network.LoadState == LoadTestState.Loading, "loading");
        _load.LoadDone.SetResult();
        await run;

        Assert.Equal([LoadTestState.Idling, LoadTestState.WaitingForLoad, LoadTestState.Loading, LoadTestState.Done], states);
        Assert.Equal(new PingTarget(NetworkTargetKind.Host, GameNetworkSamples.Server), _load.Target);
        Assert.Equal("{wifi}", _load.AdapterId);
        Assert.Same(_load.Result, _screen.Session.GameNetwork?.Load);
        Assert.Contains(network.AdviceRows, r => r.Title == EnumText.Format(AdviceId.GameNetworkBufferbloat));
    }

    [Fact]
    public async Task Cancel_during_wait_cancels_load_test()
    {
        var network = await MeasuredAsync();
        var run = network.StartLoadTestCommand.ExecuteAsync(null);
        _load.IdleDone.SetResult();
        await ScreenSession.WaitUntilAsync(() => network.LoadState == LoadTestState.WaitingForLoad, "waiting for load");

        network.CancelCommand.Execute(null);
        await run;

        Assert.Equal(Strings.NetworkLoadCancelled, network.LoadText);
        Assert.Null(_screen.Session.GameNetwork?.Load);
        Assert.True(network.StartLoadTestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_test_unavailable_without_measurement()
    {
        _store.Saved.Add(GameNetworkSamples.Measurement());
        var fresh = Network();
        var stored = Network();
        await stored.RefreshAsync(Ct);

        Assert.False(fresh.StartLoadTestCommand.CanExecute(null));
        Assert.Equal(Strings.NetworkLoadUnavailable, fresh.LoadText);
        Assert.Single(stored.Measurements);
        Assert.False(stored.StartLoadTestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_test_falls_back_to_isp_hop_when_server_is_silent()
    {
        var measurement = GameNetworkSamples.Measurement();
        measurement = measurement with
        {
            Series = [.. measurement.Series.Select(s => s.Kind == NetworkTargetKind.Host ? s with { SamplesMs = [null, null, null] } : s)],
        };
        var network = await MeasuredAsync(measurement);

        var run = network.StartLoadTestCommand.ExecuteAsync(null);
        _load.IdleDone.SetResult();
        await ScreenSession.WaitUntilAsync(() => network.LoadState == LoadTestState.WaitingForLoad, "waiting for load");
        network.CancelCommand.Execute(null);
        await run;

        Assert.Equal(new PingTarget(NetworkTargetKind.IspHop, GameNetworkSamples.IspHop), _load.Target);
    }

    [Fact]
    public async Task Refresh_shows_latest_saved_measurement()
    {
        _store.Saved.Add(Run("older", null, 0));
        _store.Saved.Add(Run("newer", "after", 10));
        var network = Network();

        await network.RefreshAsync(Ct);

        Assert.Equal(["newer", "older"], network.Measurements.Select(m => m.Id));
        Assert.Equal([Ms(2), Ms(10), Ms(28), Ms(40)], network.BreakdownRows.Select(r => r.Value.Text));
        Assert.Null(_screen.Session.GameNetwork);
    }

    [Fact]
    public async Task Store_failure_on_refresh_shows_empty_list()
    {
        var network = Network(new ThrowingStore());

        await network.RefreshAsync(Ct);

        Assert.Empty(network.Measurements);
        Assert.Empty(network.BreakdownRows);
    }

    [Fact]
    public async Task Comparison_defaults_to_unlabeled_baseline()
    {
        _store.Saved.AddRange([Run("b1", null, 0), Run("b2", null, 10), Run("f1", "first", 20), Run("f2", "first", 30),
            Run("s1", "second", 40), Run("s2", "second", 50)]);
        var network = Network();

        await network.RefreshAsync(Ct);

        Assert.NotNull(network.Baseline);
        Assert.Null(network.Baseline.Group.Label);
        Assert.Equal("second", network.Candidate?.Group.Label);
        Assert.Contains(Strings.NetworkComparisonCaveat, network.ComparisonText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comparison_with_one_run_says_too_few()
    {
        _store.Saved.AddRange([Run("b1", null, 0), Run("s1", "second", 10)]);
        var network = Network();

        await network.RefreshAsync(Ct);

        Assert.Contains(EnumText.Format(ComparisonCaveat.TooFewRuns), network.ComparisonText, StringComparison.Ordinal);
        Assert.Contains(EnumText.Format(ChangeVerdict.Inconclusive), network.ComparisonText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_candidate_updates_report_comparison()
    {
        _store.Saved.AddRange([Run("b1", null, 0), Run("b2", null, 10), Run("s1", "second", 20)]);
        var network = await MeasuredAsync(Run("s2", "second", 30));
        await network.RefreshAsync(Ct);
        var first = _screen.Session.GameNetwork?.Comparison;

        network.Candidate = network.Groups.Single(g => g.Group.Label is null);
        network.Candidate = network.Groups.Single(g => g.Group.Label == "second");

        Assert.NotNull(first);
        Assert.Equal(2, _screen.Session.GameNetwork?.Comparison?.CandidateRuns);
    }

    [Fact]
    public async Task Delete_removes_measurement_and_rebuilds_groups()
    {
        _store.Saved.AddRange([Run("b1", null, 0), Run("s1", "second", 10)]);
        var network = Network();
        await network.RefreshAsync(Ct);

        await network.DeleteMeasurementCommand.ExecuteAsync("s1");

        Assert.Equal(["b1"], network.Measurements.Select(m => m.Id));
        Assert.Equal(["b1"], _store.Saved.Select(m => m.Id));
        Assert.Single(network.Groups);
        Assert.Null(network.Candidate);
    }

    [Fact]
    public async Task Delivery_optimization_advice_opens_its_settings_page()
    {
        var consumers = Reading<IReadOnlyList<ConsumerTraffic>>.Known(
            [new ConsumerTraffic(900, "svchost", ["DoSvc"], NetworkConsumerKind.DeliveryOptimization, 500)], GameNetworkMeasurer.TrafficSource);
        var network = await MeasuredAsync(GameNetworkSamples.Measurement(consumers: consumers));

        var row = Assert.Single(network.AdviceRows, r => r.SettingsPage is not null);

        Assert.Equal("delivery-optimization", row.SettingsPage);
        Assert.Equal(EnumText.Format(AdviceId.GameNetworkDeliveryOptimization), row.Title);
    }

    public void Dispose() => _screen.Dispose();

    private sealed class ThrowingStore : IGameNetworkStore
    {
        public Task SaveAsync(GameNetworkMeasurement measurement, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<GameNetworkMeasurement>> LoadAllAsync(CancellationToken ct) =>
            throw new UnauthorizedAccessException("store directory denied");

        public Task DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task View_with_measurement_loads()
    {
        var network = await MeasuredAsync();

        Assert.Null(WpfHost.TryRender(() => new Booster.App.Views.NetworkView { DataContext = network }));
    }

    [Fact]
    public void Empty_view_loads()
    {
        Assert.Null(WpfHost.TryRender(() => new Booster.App.Views.NetworkView { DataContext = Network() }));
    }
}
