using System.ComponentModel;
using System.Globalization;
using System.IO;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RunnerStatus = Booster.Core.Recovery.MaintenanceStatus;

namespace Booster.App.ViewModels;

public sealed record ActivityRow(string Process, int ProcessId, string Read, string Write, string HardFaults);

public sealed record StartupRow(StartupEntry Entry, string Name, string Location, string State, string Memory, string Verdict, bool CanDisable,
    string? DisableHint)
{
    public override string ToString() => Name;
}

public sealed record TweakRow(TweakProposal Proposal, string Name, string State, string Evidence, bool CanApply)
{
    public override string ToString() => Name;
}

public sealed partial class SystemViewModel : ObservableObject, IPage
{
    private const string PressureSource = "ISystemPressureSource";
    private const string ExeSuffix = ".exe";

    private static readonly string[] SectionIds =
    [
        ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability, ReportSectionIds.Network,
        ReportSectionIds.Startup, ReportSectionIds.Tweaks,
    ];

    private readonly SessionReport _report;
    private readonly DiagnosticsSession _session;
    private readonly IProcessResourceSource _processes;
    private readonly ISystemPressureSource _pressure;
    private readonly NetworkTester _network;
    private readonly MaintenanceRunner _maintenance;
    private readonly IUserRegistry _registry;
    private readonly IMachineRegistry _machineRegistry;
    private readonly GameSessionCoordinator _coordinator;
    private readonly IGameExecutablePicker _picker;
    private readonly ISettingsOpener _settings;
    private readonly IBoosterConfiguration _config;
    private readonly TimeProvider _time;
    private readonly ILogger<SystemViewModel> _logger;
    private readonly SynchronizationContext? _context;
    private readonly Lock _networkGate = new();
    private CancellationTokenSource? _loop;
    private int _networkRun;
    private IReversibleAction? _pending;

    private int _request;

    public SystemViewModel(SessionReport report, DiagnosticsSession session, IProcessResourceSource processes, ISystemPressureSource pressure,
        NetworkTester network, MaintenanceRunner maintenance, IUserRegistry registry, IMachineRegistry machineRegistry,
        GameSessionCoordinator coordinator,
        IGameExecutablePicker picker, ISettingsOpener settings, LatencyViewModel latency, HagsViewModel hags, IBoosterConfiguration config,
        TimeProvider time, ILogger<SystemViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(latency);
        ArgumentNullException.ThrowIfNull(hags);
        _report = report;
        Latency = latency;
        Hags = hags;
        _session = session;
        _processes = processes;
        _pressure = pressure;
        _network = network;
        _maintenance = maintenance;
        _registry = registry;
        _machineRegistry = machineRegistry;
        _coordinator = coordinator;
        _picker = picker;
        _settings = settings;
        _config = config;
        _time = time;
        _logger = logger;
        _context = SynchronizationContext.Current;
        _report.PropertyChanged += OnReportChanged;
        Update();
    }

    public event EventHandler? OpenGameNetworkRequested;

    public LatencyViewModel Latency { get; }

    public HagsViewModel Hags { get; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportSection> Sections { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasData { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportField> Pressure { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ActivityRow> TopIo { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ActivityRow> TopHardFaults { get; private set; } = [];

    [ObservableProperty]
    public partial string? ActivityStatus { get; private set; }

    [ObservableProperty]
    public partial string? NetworkStatus { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<StartupRow> StartupRows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TweakRow> TweakRows { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMaintenanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelMaintenanceCommand))]
    public partial string? PendingConfirmation { get; private set; }

    [ObservableProperty]
    public partial string? MaintenanceStatus { get; private set; }

    public void Activate()
    {
        if (_loop is not null)
            return;

        _loop = new CancellationTokenSource();
        Pressure = [];
        TopIo = [];
        TopHardFaults = [];
        ActivityStatus = Strings.SystemActivityWaiting;
        var token = _loop.Token;
        _ = Task.Run(() => PollAsync(token), token);
        _ = Latency.RefreshAsync(CancellationToken.None);
        _ = Hags.RefreshAsync(CancellationToken.None);
    }

    public void Deactivate()
    {
        if (_loop is null)
            return;

        _loop.Cancel();
        _loop.Dispose();
        _loop = null;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var diagnostics = _config.Current.Settings.Diagnostics;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(diagnostics.ActivityIntervalMs), _time);
            Sample(diagnostics.TopProcessCount, out _);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var culture = CultureInfo.CurrentCulture;
                var pressure = PressureFields(Sample(diagnostics.TopProcessCount, out var io), culture);
                var (topIo, topFaults, status) = Activity(io, culture);
                OnUiThread(() =>
                {
                    Pressure = pressure;
                    TopIo = topIo;
                    TopHardFaults = topFaults;
                    ActivityStatus = status;
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Экран закрыт.
        }
    }

    private SystemPressure? Sample(int topCount, out Reading<ProcessIoActivity>? io)
    {
        SystemPressure? pressure = null;
        try
        {
            pressure = _pressure.Sample();
        }
        catch (Exception ex)
        {
            LogPressureFailed(ex);
        }

        try
        {
            io = _processes.SampleIo(topCount);
        }
        catch (Exception ex)
        {
            LogActivityFailed(ex);
            io = null;
        }
        return pressure;
    }

    private static List<ReportField> PressureFields(SystemPressure? pressure, CultureInfo culture)
    {
        var failed = Reading<double>.Failure(PressureSource, "sample failed");
        string Percent(double v) => SensorFieldFormatter.Percent(v, culture);
        return
        [
            new(Strings.SystemAvailableMemory, SensorFieldFormatter.Value(pressure?.AvailableMemoryPercent ?? failed, Percent)),
            new(Strings.SystemCommitPercent, SensorFieldFormatter.Value(pressure?.CommitPercent ?? failed, Percent)),
            new(Strings.SystemPageReads, SensorFieldFormatter.Value(pressure?.PageReadsPerSecond ?? failed,
                v => PerSecond(v.ToString("0.#", culture), culture))),
            new(Strings.SystemDiskBusy, SensorFieldFormatter.Value(pressure?.DiskBusyPercent ?? failed, Percent)),
            new(Strings.SystemDiskLatency, SensorFieldFormatter.Value(pressure?.DiskLatencyMs ?? failed,
                v => string.Format(culture, ReportStrings.UnitMs, v.ToString("0.#", culture)))),
            new(Strings.SystemDpcTime, SensorFieldFormatter.Value(pressure?.DpcTimePercent ?? failed,
                v => SensorFieldFormatter.FractionPercent(v, culture))),
            new(Strings.SystemInterruptTime, SensorFieldFormatter.Value(pressure?.InterruptTimePercent ?? failed,
                v => SensorFieldFormatter.FractionPercent(v, culture))),
        ];
    }

    private static (IReadOnlyList<ActivityRow> Io, IReadOnlyList<ActivityRow> Faults, string? Status) Activity(
        Reading<ProcessIoActivity>? io, CultureInfo culture)
    {
        if (io is not { } reading)
            return ([], [], ReadingFormatter.StatusText(DataStatus.Failed));
        if (reading is not { IsAvailable: true, Value: { } activity })
            return ([], [], reading.Status == DataStatus.Unknown ? Strings.SystemActivityWaiting : ReadingFormatter.StatusText(reading.Status));

        ActivityRow Row(ProcessIoUsage usage) => new(usage.Name, usage.ProcessId,
            PerSecond(ByteSizeFormatter.Format((long)usage.ReadBytesPerSecond, culture), culture),
            PerSecond(ByteSizeFormatter.Format((long)usage.WriteBytesPerSecond, culture), culture),
            PerSecond(usage.HardFaultsPerSecond.ToString("0.#", culture), culture));
        return ([.. activity.TopIo.Select(Row)], [.. activity.TopHardFaults.Select(Row)], null);
    }

    private static string PerSecond(string value, CultureInfo culture) => string.Format(culture, Strings.SystemPerSecondFormat, value);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunNetworkTestAsync(CancellationToken ct)
    {
        var run = SetNetworkStatus(Strings.SystemNetworkStarting);
        var rounds = _config.Current.Settings.Network.PingCount;
        var progress = new DirectProgress(round => OnUiThread(() =>
            ShowRound(run, string.Format(CultureInfo.CurrentCulture, Strings.SystemNetworkRoundFormat, round, rounds))));
        try
        {
            var result = await Task.Run(() => _network.RunAsync(progress, ct), ct);
            _session.SetNetworkTest(result);
            SetNetworkStatus(Strings.SystemNetworkDone);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetNetworkStatus(Strings.SystemNetworkCancelled);
        }
        catch (Exception ex)
        {
            LogNetworkTestFailed(ex);
            SetNetworkStatus(Strings.SystemNetworkFailed);
        }
    }

    private int SetNetworkStatus(string text)
    {
        lock (_networkGate)
        {
            NetworkStatus = text;
            return ++_networkRun;
        }
    }

    private void ShowRound(int run, string text)
    {
        lock (_networkGate)
        {
            if (run == _networkRun)
                NetworkStatus = text;
        }
    }

    [RelayCommand]
    private async Task RequestDisableStartupAsync(StartupRow? row, CancellationToken ct)
    {
        if (row is not { CanDisable: true })
            return;

        var request = StartRequest();
        var action = new StartupItemAction(_registry, _machineRegistry, row.Entry, _time);
        var verdict = action.Descriptor.RequiresAdmin ? StartupVerdict.NeedsAdmin : StartupVerdict.Candidate;
        var risk = Risk(StartupAnalyzer.Advise([new StartupAssessment(new StartupItem(row.Entry, null), verdict)]));
        await PrepareAsync(request, action, location: null, risk, ct);
    }

    [RelayCommand]
    private async Task RequestApplyTweakAsync(TweakRow? row, CancellationToken ct)
    {
        if (row is not { CanApply: true })
            return;

        var request = StartRequest();
        var proposal = row.Proposal;
        var valueName = proposal.ValueName;
        if (valueName is null)
        {
            if (ProcessNames.Normalize(proposal.Game) is not { } game)
            {
                MaintenanceStatus = Strings.MaintenanceGameUnknown;
                return;
            }

            var expected = game + ExeSuffix;
            if (_picker.Pick(expected) is not { } picked)
                return;
            if (!string.Equals(ProcessNames.Normalize(Path.GetFileName(picked)), game, StringComparison.OrdinalIgnoreCase))
            {
                MaintenanceStatus = string.Format(CultureInfo.CurrentCulture, Strings.MaintenanceWrongFileFormat, expected);
                return;
            }
            valueName = picked;
        }

        var location = string.Format(CultureInfo.CurrentCulture, ReportStrings.TweakLocationFormat, proposal.Definition.SubKey, valueName);
        await PrepareAsync(request, new RegistryTweakAction(_registry, proposal.Definition, valueName, _time), location,
            Risk(TweakAdvisor.Advise([proposal])), ct);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private async Task ConfirmMaintenanceAsync(CancellationToken ct)
    {
        if (_pending is not { } action)
            return;

        ClearPending();
        var gamingSessionActive = _coordinator.Status.Mode == CoordinatorMode.InSession;
        MaintenanceStatus = Strings.MaintenanceApplying;
        try
        {
            var result = await Task.Run(() => _maintenance.ApplyAsync(action, userConfirmed: true, gamingSessionActive, ct), ct);
            MaintenanceStatus = ResultText(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            MaintenanceStatus = null;
            return;
        }
        catch (Exception ex)
        {
            LogMaintenanceFailed(ex, action.Id);
            MaintenanceStatus = string.Format(CultureInfo.CurrentCulture, Strings.MaintenanceFailedFormat, ex.Message);
            return;
        }

        try
        {
            await Task.Run(() => _session.RefreshAsync(ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Экран закрыт.
        }
        catch (Exception ex)
        {
            LogRefreshFailed(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void CancelMaintenance() => ClearPending();

    [RelayCommand]
    private void OpenGameNetwork() => OpenGameNetworkRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenGamingSettings(string? page)
    {
        if (page is null || !_settings.TryOpenSettings(page))
            MaintenanceStatus = Strings.SettingsOpenFailed;
    }

    private bool HasPending() => PendingConfirmation is not null;

    private int StartRequest()
    {
        ClearPending();
        MaintenanceStatus = null;
        return _request;
    }

    private void ClearPending()
    {
        _request++;
        _pending = null;
        PendingConfirmation = null;
    }

    private async Task PrepareAsync(int request, IReversibleAction action, string? location, string? risk, CancellationToken ct)
    {
        var culture = CultureInfo.CurrentCulture;
        ActionSupport support;
        try
        {
            support = await Task.Run(() => action.CheckSupportAsync(ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogSupportCheckFailed(ex, action.Id);
            if (request == _request)
                MaintenanceStatus = string.Format(culture, Strings.MaintenanceCheckFailedFormat, ex.Message);
            return;
        }

        if (request != _request)
            return;
        if (!support.IsSupported)
        {
            MaintenanceStatus = string.Format(culture, Strings.MaintenanceNotSupportedFormat, support.Reason ?? Strings.MaintenanceNoDetail);
            return;
        }

        _pending = action;
        PendingConfirmation = string.Join(Environment.NewLine, new[] { support.Summary, location, risk }.OfType<string>());
    }

    private static string? Risk(IReadOnlyList<SystemAdvice> advice) => advice.Count > 0 ? AdviceText.Risk(advice[0]) : null;

    private static string ResultText(MaintenanceResult result)
    {
        var culture = CultureInfo.CurrentCulture;
        var detail = result.Detail ?? Strings.MaintenanceNoDetail;
        return result.Status switch
        {
            RunnerStatus.Applied => Strings.MaintenanceApplied,
            RunnerStatus.NotSupported => string.Format(culture, Strings.MaintenanceNotSupportedFormat, detail),
            RunnerStatus.Refused => string.Format(culture, Strings.MaintenanceRefusedFormat, detail),
            RunnerStatus.NotApplied => string.Format(culture, Strings.MaintenanceNotAppliedFormat, detail),
            _ => string.Format(culture, Strings.MaintenanceFailedFormat, detail),
        };
    }

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionReport.Document))
            Update();
    }

    private void Update()
    {
        if (_report.Document is not { } document)
            return;

        Sections = SectionIds.Select(document.Section).OfType<ReportSection>().ToList();
        if (_report.Report is { } report)
        {
            var culture = CultureInfo.CurrentCulture;
            StartupRows = BuildStartupRows(report.Snapshot, culture);
            TweakRows = [.. report.Tweaks.Where(p => p.State != TweakState.NotNeeded).Select(p => new TweakRow(p,
                TweakText.Name(p.Definition.Id) ?? p.Definition.Id, EnumText.Format(p.State), TweakText.Evidence(p, culture),
                CanApply: p.State == TweakState.Proposed))];
        }
        HasData = true;
    }

    private List<StartupRow> BuildStartupRows(SystemSnapshot snapshot, CultureInfo culture)
    {
        if (snapshot.System.Startup is not { IsAvailable: true, Value: { } items })
            return [];

        var current = _config.Current;
        var catalog = current.Catalog.Services;
        return [.. StartupAnalyzer.Assess(items, current.Thresholds.Startup, catalog)
            .OrderBy(a => a.Item.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a =>
            {
                var availability = StartupAnalyzer.DisableAvailability(a.Item.Entry, catalog, snapshot.Privileges.IsElevated);
                var available = availability == StartupDisableAvailability.Available;
                return new StartupRow(a.Item.Entry, a.Item.Entry.Name, EnumText.Format(a.Item.Entry.Location),
                    EnumText.Format(a.Item.Entry.State),
                    a.Item.PrivateBytes is { } bytes ? ByteSizeFormatter.Format(bytes, culture) : ReadingFormatter.StatusText(DataStatus.Unknown),
                    EnumText.Format(a.Verdict), available, available ? null : EnumText.Format(availability));
            })];
    }

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(_ => action(), null);
    }

    private sealed class DirectProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "System pressure sample failed")]
    private partial void LogPressureFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process I/O sample failed")]
    private partial void LogActivityFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Network test failed")]
    private partial void LogNetworkTestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Support check of maintenance action {ActionId} failed")]
    private partial void LogSupportCheckFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance action {ActionId} failed")]
    private partial void LogMaintenanceFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Snapshot refresh after maintenance failed")]
    private partial void LogRefreshFailed(Exception exception);
}
