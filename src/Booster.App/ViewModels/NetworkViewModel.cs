using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public enum LoadTestState { Idle, Idling, WaitingForLoad, Loading, Done }

/// <summary>
/// Экран «Сеть в игре»: замер, разбивка задержки, пинг под нагрузкой и сравнение замеров до и после изменения.
/// В отчёт идёт только замер, сделанный в этой сессии.
/// </summary>
public sealed partial class NetworkViewModel : ObservableObject, IPage
{
    private const double PercentScale = 100;

    private readonly IGameNetworkMeasurer _measurer;
    private readonly ILoadTestRunner _loadTests;
    private readonly IGameNetworkStore _store;
    private readonly DiagnosticsSession _session;
    private readonly ISettingsOpener _settings;
    private readonly IBoosterConfiguration _config;
    private readonly ILogger<NetworkViewModel> _logger;
    private readonly SynchronizationContext? _context;
    private readonly List<GameNetworkMeasurement> _all = [];
    private CancellationTokenSource? _run;
    private TaskCompletionSource? _loadGate;
    private GameNetworkMeasurement? _current;
    private GameNetworkAnalysis? _currentAnalysis;
    private LoadTestResult? _load;
    private GameNetworkComparison? _comparison;
    private GameNetworkMeasurement? _shown;
    private bool _rebuilding;

    public NetworkViewModel(IGameNetworkMeasurer measurer, ILoadTestRunner loadTests, IGameNetworkStore store, DiagnosticsSession session,
        ISettingsOpener settings, IBoosterConfiguration config, ILogger<NetworkViewModel> logger)
    {
        _measurer = measurer;
        _loadTests = loadTests;
        _store = store;
        _session = session;
        _settings = settings;
        _config = config;
        _logger = logger;
        _context = SynchronizationContext.Current;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartLoadTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMeasurementCommand))]
    public partial bool IsMeasuring { get; private set; }

    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    public partial string? PhaseText { get; private set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string? ManualServer { get; set; }

    [ObservableProperty]
    public partial string? Label { get; set; }

    [ObservableProperty]
    public partial bool HasMeasurement { get; private set; }

    [ObservableProperty]
    public partial string? ServerText { get; private set; }

    [ObservableProperty]
    public partial string? ServerNote { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<NetworkCandidateRow> Candidates { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ReportField> BreakdownRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? BreakdownNote { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<NetworkTargetRow> TargetRows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NetworkHopRow> RouteRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? RouteNote { get; private set; }

    [ObservableProperty]
    public partial string? SpikesText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportField> WifiRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? WifiText { get; private set; }

    [ObservableProperty]
    public partial bool ShowLocationHint { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<NetworkConsumerRow> ConsumerRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? ConsumersText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartLoadTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueLoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMeasurementCommand))]
    public partial LoadTestState LoadState { get; private set; }

    [ObservableProperty]
    public partial string? LoadText { get; private set; } = Strings.NetworkLoadUnavailable;

    [ObservableProperty]
    public partial IReadOnlyList<ReportField> LoadRows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MeasurementGroupRow> Groups { get; private set; } = [];

    [ObservableProperty]
    public partial MeasurementGroupRow? Baseline { get; set; }

    [ObservableProperty]
    public partial MeasurementGroupRow? Candidate { get; set; }

    [ObservableProperty]
    public partial string? ComparisonText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<NetworkAdviceRow> AdviceRows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NetworkMeasurementRow> Measurements { get; private set; } = [];

    public void Activate() => _ = RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken ct)
    {
        IReadOnlyList<GameNetworkMeasurement> loaded;
        try
        {
            loaded = await Task.Run(() => _store.LoadAllAsync(ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogLoadFailed(ex);
            loaded = [];
        }

        _all.Clear();
        _all.AddRange(loaded);
        if (_shown is null || !_all.Exists(m => m.Id == _shown.Id))
            ShowLatest();
        Rebuild(null);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task MeasureAsync()
    {
        var run = Begin();
        var ct = run.Token;
        IsMeasuring = true;
        StatusText = Strings.NetworkMeasuring;
        ProgressPercent = 0;
        var request = new GameNetworkRequest(Trimmed(ManualServer), Trimmed(Label));
        try
        {
            var result = await Task.Run(() => _measurer.MeasureAsync(request, Progress(), ct), ct);
            StatusText = Apply(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = Strings.NetworkMeasureCancelled;
        }
        catch (Exception ex)
        {
            LogMeasureFailed(ex);
            StatusText = Strings.NetworkMeasureFailed;
        }
        finally
        {
            End(run);
            IsMeasuring = false;
            PhaseText = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _run?.Cancel();

    [RelayCommand]
    private void UseCandidate(string? address)
    {
        if (!string.IsNullOrWhiteSpace(address))
            ManualServer = address;
    }

    [RelayCommand(CanExecute = nameof(CanStartLoad))]
    private async Task StartLoadTestAsync()
    {
        if (_current is not { Adapter: { } adapter } measurement || LoadTarget(measurement) is not { } target)
            return;

        var run = Begin();
        var ct = run.Token;
        var culture = CultureInfo.CurrentCulture;
        LoadState = LoadTestState.Idling;
        LoadText = Strings.NetworkLoadIdling;
        ProgressPercent = 0;
        try
        {
            var result = await Task.Run(() => _loadTests.RunAsync(target, adapter.Id, WaitForLoadAsync, Progress(), ct), ct);
            _load = result;
            LoadRows = NetworkRows.Load(result, culture);
            LoadText = Strings.NetworkLoadDone;
            LoadState = LoadTestState.Done;
            if (_shown?.Id == measurement.Id && _currentAnalysis is { } analysis)
                AdviceRows = NetworkRows.Advice(AdviceFor(analysis, result), culture);
            Publish();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LoadText = Strings.NetworkLoadCancelled;
            LoadState = _load is null ? LoadTestState.Idle : LoadTestState.Done;
        }
        catch (Exception ex)
        {
            LogLoadTestFailed(ex);
            LoadText = Strings.NetworkLoadFailed;
            LoadState = _load is null ? LoadTestState.Idle : LoadTestState.Done;
        }
        finally
        {
            End(run);
            PhaseText = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsWaitingForLoad))]
    private void ContinueLoad() => Volatile.Read(ref _loadGate)?.TrySetResult();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DeleteMeasurementAsync(string? id)
    {
        if (id is null)
            return;

        try
        {
            await Task.Run(() => _store.DeleteAsync(id, CancellationToken.None));
        }
        catch (Exception ex)
        {
            LogDeleteFailed(ex);
            StatusText = Strings.NetworkDeleteFailed;
            await RefreshAsync(CancellationToken.None);
            return;
        }

        _all.RemoveAll(m => m.Id == id);
        if (_current?.Id == id)
        {
            _current = null;
            _currentAnalysis = null;
            _load = null;
            LoadRows = [];
            LoadState = LoadTestState.Idle;
            LoadText = Strings.NetworkLoadUnavailable;
            StartLoadTestCommand.NotifyCanExecuteChanged();
        }
        if (_shown?.Id == id)
            ShowLatest();
        Rebuild(null);
    }

    [RelayCommand]
    private void OpenSettings(string? page)
    {
        if (page is null || !_settings.TryOpenSettings(page))
            StatusText = Strings.SettingsOpenFailed;
    }

    partial void OnBaselineChanged(MeasurementGroupRow? value)
    {
        if (!_rebuilding)
            UpdateComparison();
    }

    partial void OnCandidateChanged(MeasurementGroupRow? value)
    {
        if (!_rebuilding)
            UpdateComparison();
    }

    private bool IsBusy() => IsMeasuring || LoadState is LoadTestState.Idling or LoadTestState.WaitingForLoad or LoadTestState.Loading;

    private bool IsIdle() => !IsBusy();

    private bool IsWaitingForLoad() => LoadState == LoadTestState.WaitingForLoad;

    private bool CanStartLoad() => IsIdle() && _current is { Adapter: not null } measurement && LoadTarget(measurement) is not null;

    private string Apply(GameNetworkRunResult result)
    {
        switch (result.Status)
        {
            case GameNetworkRunStatus.NoGame:
                return Strings.NetworkNoGame;
            case GameNetworkRunStatus.NoServer:
                return Strings.NetworkNoServer;
            case GameNetworkRunStatus.InvalidManualServer:
                return Strings.NetworkInvalidServer;
        }
        if (result is not { Measurement: { } measurement, Analysis: { } analysis })
            return Strings.NetworkMeasureFailed;

        _current = measurement;
        _currentAnalysis = analysis;
        _load = null;
        LoadRows = [];
        LoadState = LoadTestState.Idle;
        LoadText = LoadAvailability(measurement);
        _all.RemoveAll(m => m.Id == measurement.Id);
        _all.Add(measurement);
        Show(measurement, analysis);
        Rebuild(measurement);
        Publish();
        StartLoadTestCommand.NotifyCanExecuteChanged();
        return measurement.End == MeasurementEnd.GameExited ? Strings.NetworkGameExited : Strings.NetworkMeasureDone;
    }

    private void ShowLatest()
    {
        var latest = _all.OrderByDescending(m => m.StartedAt).FirstOrDefault();
        if (latest is null)
        {
            Clear();
            return;
        }
        if (latest.Id == _current?.Id && _currentAnalysis is { } current)
        {
            Show(latest, current);
            return;
        }

        try
        {
            var thresholds = _config.Current.Thresholds;
            Show(latest, GameNetworkAnalyzer.Analyze(latest, thresholds.GameNetwork, thresholds.Network));
        }
        catch (Exception ex)
        {
            LogAnalysisFailed(ex, latest.Id);
            Clear();
        }
    }

    private void Show(GameNetworkMeasurement measurement, GameNetworkAnalysis analysis)
    {
        var culture = CultureInfo.CurrentCulture;
        _shown = measurement;
        HasMeasurement = true;
        ServerText = NetworkRows.Server(measurement.Server, culture);
        ServerNote = NetworkRows.ServerNote(measurement, analysis);
        Candidates = NetworkRows.Candidates(measurement.Server, culture);
        BreakdownRows = NetworkRows.Breakdown(analysis.Breakdown, culture);
        BreakdownNote = analysis.Breakdown.Clamped ? ReportStrings.GameNetworkClampedNote : null;
        TargetRows = NetworkRows.Targets(analysis, culture);
        RouteRows = NetworkRows.Route(measurement.Route, culture);
        RouteNote = NetworkRows.RouteNote(measurement, analysis, culture);
        SpikesText = NetworkRows.Spikes(analysis.Spikes, culture);
        WifiRows = NetworkRows.Wifi(measurement, culture);
        WifiText = NetworkRows.WifiText(measurement, culture);
        ShowLocationHint = NetworkRows.LocationDenied(measurement.Wifi);
        ConsumerRows = NetworkRows.Consumers(measurement.Consumers, culture);
        ConsumersText = NetworkRows.ConsumersText(measurement.Consumers);
        var load = measurement.Id == _current?.Id ? _load : null;
        AdviceRows = NetworkRows.Advice(AdviceFor(analysis, load), culture);
    }

    private void Clear()
    {
        _shown = null;
        HasMeasurement = false;
        ServerText = null;
        ServerNote = null;
        Candidates = [];
        BreakdownRows = [];
        BreakdownNote = null;
        TargetRows = [];
        RouteRows = [];
        RouteNote = null;
        SpikesText = null;
        WifiRows = [];
        WifiText = null;
        ShowLocationHint = false;
        ConsumerRows = [];
        ConsumersText = null;
        AdviceRows = [];
    }

    private void Rebuild(GameNetworkMeasurement? fresh)
    {
        var culture = CultureInfo.CurrentCulture;
        Measurements = [.. _all.OrderByDescending(m => m.StartedAt).Select(m => NetworkRows.Measurement(m, culture))];
        var groups = GameNetworkComparer.Group(_all).Select(g => NetworkRows.Group(g, culture)).ToArray();

        _rebuilding = true;
        try
        {
            Groups = groups;
            var baseline = Same(groups, Baseline) ?? DefaultBaseline(groups);
            Baseline = baseline;
            var freshGroup = fresh is null ? null : groups.FirstOrDefault(g => g.Group.Runs.Any(r => r.Id == fresh.Id));
            Candidate = (freshGroup is not null && freshGroup != baseline && SameServer(freshGroup, baseline) ? freshGroup : null)
                ?? Same(groups, Candidate)
                ?? DefaultCandidate(groups, baseline);
        }
        finally
        {
            _rebuilding = false;
        }
        UpdateComparison();
    }

    private MeasurementGroupRow? DefaultBaseline(IReadOnlyList<MeasurementGroupRow> groups)
    {
        var plain = groups.Where(g => g.Group.Label is null && g.Group.ActiveActions.Count == 0).ToArray();
        return plain.FirstOrDefault(g => string.Equals(g.Group.Server, _shown?.Server.Address, StringComparison.Ordinal))
            ?? plain.FirstOrDefault();
    }

    private static MeasurementGroupRow? DefaultCandidate(IReadOnlyList<MeasurementGroupRow> groups, MeasurementGroupRow? baseline) =>
        baseline is null ? null : groups.FirstOrDefault(g => g != baseline && SameServer(g, baseline));

    private static bool SameServer(MeasurementGroupRow group, MeasurementGroupRow? other) =>
        other is not null && string.Equals(group.Group.Server, other.Group.Server, StringComparison.Ordinal);

    private static MeasurementGroupRow? Same(IReadOnlyList<MeasurementGroupRow> groups, MeasurementGroupRow? previous) =>
        previous is null
            ? null
            : groups.FirstOrDefault(g => string.Equals(g.Group.Server, previous.Group.Server, StringComparison.Ordinal)
                && string.Equals(g.Group.Label, previous.Group.Label, StringComparison.Ordinal)
                && g.Group.ActiveActions.SequenceEqual(previous.Group.ActiveActions, StringComparer.Ordinal));

    private void UpdateComparison()
    {
        var thresholds = _config.Current.Thresholds;
        var sameGroup = Baseline is not null && Baseline == Candidate;
        _comparison = Baseline is { } baseline && Candidate is { } candidate && !sameGroup
            ? GameNetworkComparer.Compare(baseline.Group, candidate.Group, thresholds.GameNetwork, thresholds.Network)
            : null;
        ComparisonText = NetworkRows.Comparison(_comparison, sameGroup, CultureInfo.CurrentCulture);
        Publish();
    }

    private void Publish()
    {
        if (_current is { } measurement && _currentAnalysis is { } analysis)
            _session.SetGameNetwork(GameNetworkReportView.From(measurement, analysis, _load, _comparison));
    }

    private static IReadOnlyList<SystemAdvice> AdviceFor(GameNetworkAnalysis analysis, LoadTestResult? load) =>
        load is null ? analysis.Advice : [.. analysis.Advice, .. GameNetworkAdvisor.AdviseLoad(load)];

    private static string? LoadAvailability(GameNetworkMeasurement measurement)
    {
        if (LoadTarget(measurement) is null)
            return Strings.NetworkLoadNoTarget;
        return measurement.Adapter is null ? Strings.NetworkLoadNoAdapter : null;
    }

    /// <summary>Пинг под нагрузкой идёт до самой дальней цели, которая отвечала во время замера.</summary>
    private static PingTarget? LoadTarget(GameNetworkMeasurement measurement)
    {
        var end = measurement.Series.FirstOrDefault(s => s.Kind is NetworkTargetKind.Host or NetworkTargetKind.LastHop);
        if (end is not null && end.SamplesMs.Any(v => v is not null))
            return new PingTarget(end.Kind, end.Address);
        var isp = measurement.Series.FirstOrDefault(s => s.Kind == NetworkTargetKind.IspHop);
        if (isp is not null && isp.SamplesMs.Any(v => v is not null))
            return new PingTarget(isp.Kind, isp.Address);
        var gateway = measurement.Series.FirstOrDefault(s => s.Kind == NetworkTargetKind.Gateway);
        return gateway is null ? null : new PingTarget(gateway.Kind, gateway.Address);
    }

    private async Task WaitForLoadAsync(CancellationToken ct)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _loadGate, gate);
        OnUiThread(() =>
        {
            LoadState = LoadTestState.WaitingForLoad;
            LoadText = Strings.NetworkLoadHint;
        });
        try
        {
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _loadGate, null);
        }
        OnUiThread(() =>
        {
            LoadState = LoadTestState.Loading;
            LoadText = Strings.NetworkLoadLoading;
        });
    }

    private DirectProgress Progress() => new(p => OnUiThread(() => ShowProgress(p)));

    private void ShowProgress(GameNetworkProgress progress)
    {
        if (!IsBusy())
            return;

        var culture = CultureInfo.CurrentCulture;
        PhaseText = progress.Phase switch
        {
            GameNetworkPhase.FindingGame => Strings.NetworkPhaseFindingGame,
            GameNetworkPhase.DetectingServer => Strings.NetworkPhaseDetectingServer,
            GameNetworkPhase.Tracing => Strings.NetworkPhaseTracing,
            GameNetworkPhase.Measuring => string.Format(culture, Strings.NetworkPhaseMeasuringFormat, progress.Done, progress.Total),
            _ => Strings.NetworkPhaseSaving,
        };
        ProgressPercent = progress.Total > 0 ? PercentScale * progress.Done / progress.Total : 0;
    }

    private CancellationTokenSource Begin()
    {
        var run = new CancellationTokenSource();
        _run = run;
        return run;
    }

    private void End(CancellationTokenSource run)
    {
        Interlocked.CompareExchange(ref _run, null, run);
        run.Dispose();
    }

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(_ => action(), null);
    }

    private sealed class DirectProgress(Action<GameNetworkProgress> report) : IProgress<GameNetworkProgress>
    {
        public void Report(GameNetworkProgress value) => report(value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network measurements could not be loaded")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Game network measurement failed")]
    private partial void LogMeasureFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ping under load failed")]
    private partial void LogLoadTestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network measurement could not be deleted")]
    private partial void LogDeleteFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network measurement {Id} could not be analyzed")]
    private partial void LogAnalysisFailed(Exception exception, string id);
}
