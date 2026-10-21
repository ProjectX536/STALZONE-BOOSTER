using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Platform.Windows.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public enum CaptureReadiness
{
    Checking,
    NotInstalled,
    NeedsElevation,
    Ready,

    /// <summary>Проверка сама завершилась ошибкой; подробности в журнале.</summary>
    CheckFailed,
}

/// <summary>
/// Производительность: серия бенчмарка через PresentMon, её ход, история серий и сравнение с базой. Booster не трогает
/// процесс игры — только запускает PresentMon и читает его CSV. Показанная серия хранится в <see cref="DiagnosticsSession"/>
/// и попадает в отчёт.
/// </summary>
public sealed partial class PerformanceViewModel : ObservableObject, IPage
{
    private const string ExecutableExtension = ".exe";

    /// <summary>Обратный отсчёт показывается в целых секундах — чаще обновлять незачем.</summary>
    private static readonly TimeSpan ProgressRefresh = TimeSpan.FromSeconds(1);

    private readonly IFrameCaptureProvider _capture;
    private readonly IProcessListSource _processes;
    private readonly DiagnosticsSession _session;
    private readonly SessionReport _report;
    private readonly BenchmarkRunner _runner;
    private readonly IBenchmarkStore _store;
    private readonly GameSessionCoordinator _coordinator;
    private readonly IBenchmarkCuePlayer _cues;
    private readonly IBoosterConfiguration _config;
    private readonly TimeProvider _time;
    private readonly ILogger<PerformanceViewModel> _logger;
    private readonly SynchronizationContext? _context;

    /// <summary>Ход серии приходит из пула потоков и от таймера; текст считается под замком по последней фазе.</summary>
    private readonly Lock _progressLock = new();
    private BenchmarkProgress? _progress;
    private string? _progressText;
    private bool _historyLoaded;

    public PerformanceViewModel(IFrameCaptureProvider capture, IProcessListSource processes, DiagnosticsSession session, SessionReport report,
        BenchmarkRunner runner, IBenchmarkStore store, GameSessionCoordinator coordinator, IBenchmarkCuePlayer cues, IBoosterConfiguration config,
        TimeProvider time, ILogger<PerformanceViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(config);
        _capture = capture;
        _processes = processes;
        _session = session;
        _report = report;
        _runner = runner;
        _store = store;
        _coordinator = coordinator;
        _cues = cues;
        _config = config;
        _time = time;
        _logger = logger;
        _context = SynchronizationContext.Current;

        var defaults = config.Current.Settings.Benchmark;
        RunCountText = Text(defaults.RunCount);
        RunSecondsText = Text(defaults.RunSeconds);
        PauseSecondsText = Text(defaults.PauseSeconds);
        StartDelayText = Text(defaults.StartDelaySeconds);
        SoundCues = defaults.SoundCues;
        LimitsText = string.Format(CultureInfo.CurrentCulture, Strings.SeriesLimitsFormat,
            SettingsNormalizer.MinimumRunCount, SettingsNormalizer.MaximumRunCount,
            SettingsNormalizer.MinimumRunSeconds, SettingsNormalizer.MaximumRunSeconds,
            SettingsNormalizer.MinimumPauseSeconds, SettingsNormalizer.MaximumPauseSeconds,
            SettingsNormalizer.MinimumStartDelaySeconds, SettingsNormalizer.MaximumStartDelaySeconds,
            config.Current.Thresholds.Comparison.MinimumRuns);
        _report.PropertyChanged += OnReportChanged;
        Update();
    }

    public ObservableCollection<ProcessEntry> Processes { get; } = [];

    /// <summary>Сохранённые серии, новые первыми.</summary>
    public ObservableCollection<BenchmarkHistoryItem> History { get; } = [];

    /// <summary>true — цель выбирается из списка окон, false — вводится имя exe (игра ещё не запущена).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool UseWindowList { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial ProcessEntry? SelectedProcess { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string ExecutableName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string RunCountText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string RunSecondsText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string PauseSecondsText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string StartDelayText { get; set; }

    /// <summary>Допустимые значения полей серии — те же, что у нормализатора настроек.</summary>
    public string LimitsText { get; }

    /// <summary>Заметки пользователя: Booster не читает настройки игры, их записывает человек.</summary>
    [ObservableProperty]
    public partial string GraphicsProfile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Scene { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WindowMode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SoundCues { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial CaptureReadiness Readiness { get; private set; }

    /// <summary>Найденный PresentMon: версия и путь.</summary>
    [ObservableProperty]
    public partial string? PresentMonText { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Обратный отсчёт текущей фазы серии; null, когда серия не идёт.</summary>
    [ObservableProperty]
    public partial string? ProgressText { get; private set; }

    /// <summary>Раздел бенчмарка из отчёта: серия, условия, прогоны, итог, ограничения, рекомендации.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReportBlock> BenchmarkBlocks { get; private set; } = [];

    /// <summary>Раздел сравнения из отчёта; пуст, если базы нет.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReportBlock> ComparisonBlocks { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetBaselineCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskDeleteCommand))]
    public partial BenchmarkHistoryItem? SelectedSeries { get; set; }

    /// <summary>База сравнения, выбранная пользователем; null — предыдущая серия той же игры.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaselineText))]
    [NotifyCanExecuteChangedFor(nameof(ClearBaselineCommand))]
    public partial BenchmarkHistoryItem? Baseline { get; private set; }

    public string BaselineText => Baseline is { } baseline
        ? string.Format(CultureInfo.CurrentCulture, Strings.BaselineFormat, baseline.StartedAt, baseline.Target, baseline.Change)
        : Strings.BaselineAuto;

    /// <summary>Открыта панель подтверждения удаления — вместо системного диалога.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; private set; }

    public void Activate()
    {
        if (CheckReadinessCommand.CanExecute(null))
            CheckReadinessCommand.Execute(null);
        if (RefreshProcessesCommand.CanExecute(null))
            RefreshProcessesCommand.Execute(null);
        if (!_historyLoaded && RefreshHistoryCommand.CanExecute(null))
            RefreshHistoryCommand.Execute(null);
    }

    [RelayCommand]
    private async Task CheckReadinessAsync(CancellationToken ct)
    {
        Readiness = CaptureReadiness.Checking;
        try
        {
            var availability = await Task.Run(() => _capture.CheckAvailabilityAsync(ct), ct);
            Readiness = !availability.IsInstalled ? CaptureReadiness.NotInstalled
                : availability.Mode == FrameCaptureMode.ElevationPrompt ? CaptureReadiness.NeedsElevation
                : CaptureReadiness.Ready;
            PresentMonText = availability.IsInstalled
                ? string.Format(CultureInfo.CurrentCulture, Strings.PresentMonFound, availability.Version ?? Strings.VersionUnknown,
                    availability.ExecutablePath)
                : null;
        }
        catch (Exception ex)
        {
            LogReadinessFailed(ex);
            Readiness = CaptureReadiness.CheckFailed;
            PresentMonText = null;
        }
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        var selectedId = SelectedProcess?.ProcessId;
        try
        {
            var entries = await Task.Run(_processes.GetWindowedProcesses);
            Processes.Clear();
            foreach (var entry in entries)
                Processes.Add(entry);
            SelectedProcess = Processes.FirstOrDefault(p => p.ProcessId == selectedId);
        }
        catch (Exception ex)
        {
            LogProcessListFailed(ex);
        }
    }

    private bool CanStart() =>
        Readiness is CaptureReadiness.Ready or CaptureReadiness.NeedsElevation
        && TryGetSchedule(out _)
        && (UseWindowList ? SelectedProcess is not null : NormalizeExecutableName(ExecutableName) is not null);

    /// <summary>
    /// Серия одним захватом PresentMon в пуле потоков. Отмена останавливает его, а не бросает исключение: серия со статусом
    /// «Отменён» показывается, но без годных прогонов в историю не попадает.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanStart))]
    private async Task StartAsync(CancellationToken ct)
    {
        if (!TryGetSchedule(out var schedule))
            return;

        var notes = new BenchmarkNotes(Note(GraphicsProfile), Note(Scene), Note(WindowMode), Note(ChangeNote));
        // Профиль отмечается, только если его сессия идёт сейчас: иначе условия серии были бы выдуманы.
        var activeProfile = _coordinator.Status is { Mode: CoordinatorMode.InSession, Plan: { } plan } ? plan.Profile.Name : null;
        var request = UseWindowList && SelectedProcess is { } process
            ? new BenchmarkRequest(null, process.ProcessId, schedule, notes, activeProfile)
            : new BenchmarkRequest(NormalizeExecutableName(ExecutableName), null, schedule, notes, activeProfile);
        var cues = SoundCues;
        StatusMessage = null;

        BenchmarkSeries series;
        var ticker = _time.CreateTimer(_ => RefreshProgress(), null, ProgressRefresh, ProgressRefresh);
        try
        {
            var progress = new DirectProgress(p => OnProgress(p, cues));
            series = await Task.Run(() => _runner.RunAsync(request, progress, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogSeriesFailed(ex);
            StatusMessage = Strings.MeasurementFailed;
            return;
        }
        finally
        {
            ticker.Dispose();
            EndProgress(cues);
        }

        await ShowNewSeriesAsync(series);
    }

    [RelayCommand]
    private async Task RefreshHistoryAsync(CancellationToken ct)
    {
        try
        {
            var all = await Task.Run(() => _store.LoadAllAsync(ct), ct);
            _historyLoaded = true;
            ReplaceHistory(all, SelectedSeries?.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHistoryFailed(ex);
            StatusMessage = Strings.HistoryLoadFailed;
        }
    }

    private bool HasSelection() => SelectedSeries is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetBaseline()
    {
        Baseline = SelectedSeries;
        if (SelectedSeries is { } selected)
            ShowSeries(selected.Series);
    }

    private bool HasBaseline() => Baseline is not null;

    [RelayCommand(CanExecute = nameof(HasBaseline))]
    private void ClearBaseline()
    {
        Baseline = null;
        if (SelectedSeries is { } selected)
            ShowSeries(selected.Series);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    /// <summary>Подтверждённое удаление выбранной серии из истории: файл серии удаляется.</summary>
    [RelayCommand]
    private async Task DeleteSeriesAsync(CancellationToken ct)
    {
        IsConfirmingDelete = false;
        if (SelectedSeries is not { } item)
            return;

        try
        {
            await Task.Run(() => _store.DeleteAsync(item.Id, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeleteFailed(ex);
            StatusMessage = Strings.DeleteFailed;
            return;
        }

        if (Baseline?.Id == item.Id)
            Baseline = null;
        SelectedSeries = null;
        History.Remove(item);
    }

    partial void OnSelectedSeriesChanged(BenchmarkHistoryItem? value)
    {
        IsConfirmingDelete = false;
        if (value is not null)
            ShowSeries(value.Series);
    }

    /// <summary>
    /// Серия с годными прогонами сохраняется в историю и выбирается в ней; без годных — только показывается: сравнивать её
    /// не с чем, а история засорялась бы пустыми записями.
    /// </summary>
    private async Task ShowNewSeriesAsync(BenchmarkSeries series)
    {
        if (!series.HasValidRuns)
        {
            SelectedSeries = null;
            ShowSeries(series);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Strings.SeriesNotSavedFormat, EnumText.Format(series.Status));
            return;
        }

        BenchmarkSeries saved;
        try
        {
            saved = await Task.Run(() => _store.SaveAsync(series, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex);
            SelectedSeries = null;
            ShowSeries(series);
            StatusMessage = Strings.SeriesSaveFailed;
            return;
        }

        IReadOnlyList<BenchmarkSeries> all;
        try
        {
            all = await Task.Run(() => _store.LoadAllAsync(CancellationToken.None), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogHistoryFailed(ex);
            all = [saved, .. History.Select(h => h.Series)];
        }

        ReplaceHistory(all, saved.Id);
        if (SelectedSeries?.Id != saved.Id)
            ShowSeries(saved);

        // Результат показывается разделом отчёта; без снимка отчёта нет, и виден только статус.
        StatusMessage = _session.Snapshot is null
            ? string.Format(CultureInfo.CurrentCulture, Strings.MeasurementStatusOnly, EnumText.Format(series.Status))
            : null;
    }

    /// <summary>Список истории заново; выбор и база сохраняются, если их серии ещё есть.</summary>
    private void ReplaceHistory(IReadOnlyList<BenchmarkSeries> all, string? selectId)
    {
        var baselineId = Baseline?.Id;
        SelectedSeries = null;
        History.Clear();
        foreach (var series in all)
            History.Add(BenchmarkHistoryItem.From(series, CultureInfo.CurrentCulture));
        Baseline = History.FirstOrDefault(h => h.Id == baselineId);
        SelectedSeries = History.FirstOrDefault(h => h.Id == selectId);
    }

    /// <summary>
    /// Серия в отчёт вместе со сравнением: база — выбранная пользователем серия той же игры, иначе предыдущая серия этой
    /// игры с годными прогонами. Цепочка — соседние серии той же игры до показанной включительно.
    /// </summary>
    private void ShowSeries(BenchmarkSeries series)
    {
        var settings = _config.Current.Thresholds.Comparison;
        if (!series.HasValidRuns)
        {
            _session.SetBenchmark(new BenchmarkReport(series, null, [], settings));
            return;
        }

        var sameGame = History.Select(h => h.Series)
            .Where(s => s.Id != series.Id && SameTarget(s, series))
            .Append(series)
            .ToList();
        var baseline = Baseline?.Series is { } chosen && chosen.Id != series.Id && SameTarget(chosen, series)
            ? chosen
            : sameGame.Where(s => s.HasValidRuns && s.StartedAt < series.StartedAt).MaxBy(s => s.StartedAt);
        var comparison = baseline is null ? null : BenchmarkComparer.Compare(baseline, series, settings);
        var chain = BenchmarkComparer.Chain(sameGame.Where(s => s.StartedAt <= series.StartedAt).ToList(), settings);
        _session.SetBenchmark(new BenchmarkReport(series, comparison, chain, settings));
    }

    private static bool SameTarget(BenchmarkSeries a, BenchmarkSeries b) =>
        string.Equals(a.Conditions.Target, b.Conditions.Target, StringComparison.OrdinalIgnoreCase);

    /// <summary>Сигнал звучит при входе в прогон и при выходе из него — в паузу, в обработку или в следующий прогон.</summary>
    private void OnProgress(BenchmarkProgress progress, bool cues)
    {
        lock (_progressLock)
        {
            var wasRun = RunIndex(_progress);
            var isRun = RunIndex(progress);
            if (cues && wasRun is not null && wasRun != isRun)
                _cues.RunEnded();
            if (cues && isRun is not null && wasRun != isRun)
                _cues.RunStarted();
            _progress = progress;
            _progressText = FormatProgress(progress);
        }

        PublishProgress();
    }

    private void RefreshProgress()
    {
        lock (_progressLock)
            _progressText = _progress is { } progress ? FormatProgress(progress) : null;
        PublishProgress();
    }

    /// <summary>Серия закончилась или отменена посреди прогона — конец прогона тоже озвучивается.</summary>
    private void EndProgress(bool cues)
    {
        lock (_progressLock)
        {
            if (cues && RunIndex(_progress) is not null)
                _cues.RunEnded();
            _progress = null;
            _progressText = null;
        }

        ProgressText = null;
    }

    /// <summary>В UI-поток уходит не значение, а чтение последнего текста: устаревшая отправка не перезапишет новый.</summary>
    private void PublishProgress() => OnUiThread(() => ProgressText = Volatile.Read(ref _progressText));

    private static int? RunIndex(BenchmarkProgress? progress) => progress is { Phase: BenchmarkPhase.Run } p ? p.RunIndex : null;

    private string? FormatProgress(BenchmarkProgress progress)
    {
        var left = progress.EndsAt is { } endsAt ? Math.Max(0, (int)Math.Ceiling((endsAt - _time.GetLocalNow()).TotalSeconds)) : 0;
        return progress.Phase switch
        {
            BenchmarkPhase.StartingCapture => Strings.ProgressStartingCapture,
            BenchmarkPhase.StartDelay => string.Format(CultureInfo.CurrentCulture, Strings.ProgressStartDelayFormat, 1, progress.RunCount, left),
            BenchmarkPhase.Run => string.Format(CultureInfo.CurrentCulture, Strings.ProgressRunFormat, progress.RunIndex + 1, progress.RunCount, left),
            BenchmarkPhase.Pause => string.Format(CultureInfo.CurrentCulture, Strings.ProgressPauseFormat, progress.RunIndex + 2, left),
            BenchmarkPhase.Processing => Strings.ProgressProcessing,
            _ => null,
        };
    }

    /// <summary>Имя файла exe: путь отбрасывается, расширение .exe дописывается. null — если имени нет.</summary>
    internal static string? NormalizeExecutableName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var name = Path.GetFileName(input.Trim().Trim('"'));
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? name : name + ExecutableExtension;
    }

    /// <summary>Поля серии в пределах нормализатора настроек; иначе серию не начать.</summary>
    private bool TryGetSchedule([NotNullWhen(true)] out BenchmarkSchedule? schedule)
    {
        schedule = null;
        if (!TryParse(RunCountText, SettingsNormalizer.MinimumRunCount, SettingsNormalizer.MaximumRunCount, out var runs)
            || !TryParse(RunSecondsText, SettingsNormalizer.MinimumRunSeconds, SettingsNormalizer.MaximumRunSeconds, out var seconds)
            || !TryParse(PauseSecondsText, SettingsNormalizer.MinimumPauseSeconds, SettingsNormalizer.MaximumPauseSeconds, out var pause)
            || !TryParse(StartDelayText, SettingsNormalizer.MinimumStartDelaySeconds, SettingsNormalizer.MaximumStartDelaySeconds, out var delay))
            return false;

        schedule = new BenchmarkSchedule(runs, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(pause), TimeSpan.FromSeconds(delay));
        return true;
    }

    private static bool TryParse(string? text, int minimum, int maximum, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= minimum && value <= maximum;

    private static string? Note(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(_ => action(), null);
    }

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionReport.Document))
            Update();
    }

    private void Update()
    {
        var document = _report.Document;
        BenchmarkBlocks = document?.Section(ReportSectionIds.Benchmark)?.Blocks ?? [];
        ComparisonBlocks = document?.Section(ReportSectionIds.Comparison)?.Blocks ?? [];
    }

    /// <summary>Ход серии без перехода в UI-поток: сигналы и текст считаются сразу, в порядке фаз.</summary>
    private sealed class DirectProgress(Action<BenchmarkProgress> report) : IProgress<BenchmarkProgress>
    {
        public void Report(BenchmarkProgress value) => report(value);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "PresentMon availability check failed")]
    private partial void LogReadinessFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window process list failed")]
    private partial void LogProcessListFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Benchmark series failed")]
    private partial void LogSeriesFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Benchmark series could not be saved")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark history could not be read")]
    private partial void LogHistoryFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Benchmark series could not be deleted")]
    private partial void LogDeleteFailed(Exception exception);
}
