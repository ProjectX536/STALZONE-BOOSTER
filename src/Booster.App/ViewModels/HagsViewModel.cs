using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Hags;
using Booster.Core.Reports;
using Booster.Core.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

/// <summary>
/// Панель A/B-теста HAGS экрана «Система». Booster HAGS не переключает: панель открывает страницу «Параметров»,
/// а пользователь переключает значение и перезагружает компьютер сам. Каждый прочитанный вид уходит в
/// <see cref="DiagnosticsSession"/> для отчёта. Операции идут по одной.
/// </summary>
public sealed partial class HagsViewModel(HagsExperimentService experiment, DiagnosticsSession session, ISettingsOpener opener,
    ILogger<HagsViewModel> logger) : ObservableObject
{
    private const string GraphicsSettingsPage = "display-advancedgraphics";

    /// <summary>Операция или чтение идёт: обновление при открытии экрана их не перебивает.</summary>
    private bool _running;

    /// <summary>null — состояние ещё не прочитано или чтение не удалось.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartExperimentCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCandidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial HagsExperimentView? View { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartExperimentCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCandidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Значение HAGS в реестре сейчас.</summary>
    [ObservableProperty]
    public partial string? CurrentText { get; private set; }

    /// <summary>Что делать дальше: по одному тексту на <see cref="HagsCheck"/>.</summary>
    [ObservableProperty]
    public partial string? StepText { get; private set; }

    /// <summary>Только для завершённого теста: ручная рекомендация, изменение метрик и оговорки сравнения.</summary>
    [ObservableProperty]
    public partial string? VerdictText { get; private set; }

    /// <summary>Итог или отказ последней операции.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    /// <summary>Перечитывает состояние: пока экран был закрыт, компьютер мог перезагрузиться. Во время операции ничего не делает.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_running)
            return;

        _running = true;
        IsBusy = true;
        try
        {
            await LoadAsync(ct);
        }
        finally
        {
            _running = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartExperimentAsync(CancellationToken ct) => RunAsync("start", async () =>
        await Task.Run(() => experiment.StartAsync(ct), ct) is { } refusal ? StartRefusalText(refusal) : Strings.HagsStarted, ct);

    /// <summary>Только открывает страницу; переключает HAGS пользователь.</summary>
    [RelayCommand]
    private void OpenGraphicsSettings()
    {
        if (!opener.TryOpenSettings(GraphicsSettingsPage))
            Status = Strings.SettingsOpenFailed;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CheckAsync(CancellationToken ct) => RunAsync("check", () => Task.FromResult<string?>(null), ct);

    [RelayCommand(CanExecute = nameof(CanTakeCandidate))]
    private Task TakeCandidateAsync(CancellationToken ct)
    {
        var target = View?.Experiment?.Target;
        return RunAsync("candidate", async () => await Task.Run(() => experiment.TakeCandidateAsync(ct), ct) is { } refusal
            ? CandidateRefusalText(refusal, target)
            : Strings.HagsCandidateTaken, ct);
    }

    /// <summary>Удаляет только файл теста: серии остаются в истории.</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private Task ResetAsync(CancellationToken ct) => RunAsync("reset", async () =>
    {
        await Task.Run(() => experiment.ResetAsync(ct), ct);
        return Strings.HagsResetDone;
    }, ct);

    private bool IsIdle() => !IsBusy;

    private bool CanStart() => !IsBusy && View?.Check == HagsCheck.NotStarted;

    private bool CanTakeCandidate() => !IsBusy && View?.Check == HagsCheck.ReadyForCandidate;

    private bool CanReset() => !IsBusy && View is { Check: not HagsCheck.NotStarted };

    /// <summary>Операция в пуле, итог — в <see cref="Status"/>; затем состояние перечитывается, что бы ни случилось.</summary>
    private async Task RunAsync(string operation, Func<Task<string?>> run, CancellationToken ct)
    {
        _running = true;
        IsBusy = true;
        try
        {
            try
            {
                Status = await run();
            }
            catch (Exception ex)
            {
                LogOperationFailed(ex, operation);
                Status = string.Format(CultureInfo.CurrentCulture, Strings.HagsErrorFormat, ex.Message);
            }

            await LoadAsync(ct);
        }
        finally
        {
            _running = false;
            IsBusy = false;
        }
    }

    /// <summary>Сбой чтения убирает вид: без него ни начать, ни взять серию, ни сбросить нельзя — только проверить снова.</summary>
    private async Task LoadAsync(CancellationToken ct)
    {
        HagsExperimentView view;
        try
        {
            view = await Task.Run(() => experiment.GetAsync(ct), ct);
        }
        catch (Exception ex)
        {
            LogReadFailed(ex);
            View = null;
            CurrentText = StepText = VerdictText = null;
            Status = string.Format(CultureInfo.CurrentCulture, Strings.HagsErrorFormat, ex.Message);
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        View = view;
        CurrentText = string.Format(culture, Strings.HagsCurrentFormat, ReadingFormatter.Format(view.Current, Value));
        StepText = StepTextOf(view, culture);
        VerdictText = VerdictTextOf(view, culture);
        session.UpdateHags(view);
    }

    private static string Value(bool enabled) => enabled ? Strings.HagsEnabled : Strings.HagsDisabled;

    /// <summary>Значение кандидата — противоположное базе: переключение засчитывается, только когда значение сменилось.</summary>
    private static string StepTextOf(HagsExperimentView view, CultureInfo culture) => (view.Check, view.Experiment) switch
    {
        (HagsCheck.NotStarted, _) => Strings.HagsStep_NotStarted,
        (HagsCheck.Unreadable, _) => Strings.HagsStep_Unreadable,
        (HagsCheck.WaitingForSwitch, { } e) => string.Format(culture, Strings.HagsStep_WaitingForSwitch, Value(e.BaselineEnabled)),
        (HagsCheck.NeedsRestart, _) => Strings.HagsStep_NeedsRestart,
        (HagsCheck.ReadyForCandidate, { SwitchedBootTime: { } boot } e) => string.Format(culture, Strings.HagsStep_ReadyForCandidate,
            Value(!e.BaselineEnabled), boot.ToLocalTime().ToString("g", culture), e.Target),
        (HagsCheck.SwitchedBack, { } e) => string.Format(culture, Strings.HagsStep_SwitchedBack, Value(e.BaselineEnabled)),
        (HagsCheck.BaselineMissing, _) => Strings.HagsStep_BaselineMissing,
        (HagsCheck.CandidateMissing, _) => Strings.HagsStep_CandidateMissing,
        (HagsCheck.Completed, { } e) => string.Format(culture, Strings.HagsStep_Completed, Value(e.BaselineEnabled), Value(!e.BaselineEnabled)),
        _ => Strings.HagsStep_StateUnknown,
    };

    /// <summary>Рекомендация только по этим сериям; затем изменение среднего FPS и 1% low и оговорки <see cref="BenchmarkComparer"/>.</summary>
    private static string? VerdictTextOf(HagsExperimentView view, CultureInfo culture)
    {
        if (view is not { Verdict: { } verdict, Comparison: { } comparison, Experiment: { } e })
            return null;

        var before = Value(e.BaselineEnabled);
        var after = Value(!e.BaselineEnabled);
        List<string> lines =
        [
            verdict switch
            {
                HagsVerdict.Keep => string.Format(culture, Strings.HagsVerdict_Keep, after),
                HagsVerdict.Revert => string.Format(culture, Strings.HagsVerdict_Revert, after, before),
                HagsVerdict.NoDifference => Strings.HagsVerdict_NoDifference,
                _ => Strings.HagsVerdict_Inconclusive,
            },
        ];
        if (Change(comparison, ComparedMetric.AverageFps, culture) is { } fps
            && Change(comparison, ComparedMetric.OnePercentLowFps, culture) is { } low)
            lines.Add(string.Format(culture, Strings.HagsChangeFormat, fps, low));
        if (comparison.Caveats.Count > 0)
        {
            lines.Add(Strings.HagsCaveatsHeading);
            lines.AddRange(comparison.Caveats.Select(caveat => string.Format(culture, Strings.HagsCaveatFormat, EnumText.Format(caveat))));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Знак всегда виден, минус — типографский, как в отчёте.</summary>
    private static string? Change(BenchmarkComparison comparison, ComparedMetric metric, CultureInfo culture) =>
        comparison.Metrics.FirstOrDefault(m => m.Metric == metric) is { } found
            ? string.Format(culture, ReportStrings.SignedPercentFormat, found.ChangePercent.ToString("+0.0;−0.0;0.0", culture))
            : null;

    private static string StartRefusalText(HagsStartRefusal refusal) => refusal switch
    {
        HagsStartRefusal.AlreadyStarted => Strings.HagsStartRefusal_AlreadyStarted,
        HagsStartRefusal.NeedsRestart => Strings.HagsStartRefusal_NeedsRestart,
        HagsStartRefusal.NoCompletedSeries => Strings.HagsStartRefusal_NoCompletedSeries,
        HagsStartRefusal.SeriesBeforeBoot => Strings.HagsStartRefusal_SeriesBeforeBoot,
        _ => Strings.HagsStartRefusal_StateUnknown,
    };

    private static string CandidateRefusalText(HagsCandidateRefusal refusal, string? target) => refusal switch
    {
        HagsCandidateRefusal.SwitchedBack => Strings.HagsCandidateRefusal_SwitchedBack,
        HagsCandidateRefusal.NoCandidateSeries => string.Format(CultureInfo.CurrentCulture, Strings.HagsCandidateRefusal_NoCandidateSeries,
            target ?? ReadingFormatter.StatusText(DataStatus.Unknown)),
        _ => Strings.HagsCandidateRefusal_NotReady,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "HAGS experiment {Operation} failed")]
    private partial void LogOperationFailed(Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HAGS experiment state could not be read")]
    private partial void LogReadFailed(Exception exception);
}
