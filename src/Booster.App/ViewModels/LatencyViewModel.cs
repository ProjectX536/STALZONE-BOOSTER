using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Latency;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

/// <summary>
/// Панель «Задержки драйверов» экрана «Система»: трасса WPR только по кнопке. Запуск, остановка и отмена идут через
/// UAC, напоминание о незавершённой записи — по маркеру <see cref="TraceRecorder"/>. Живые DPC и прерывания показывает
/// <see cref="SystemViewModel.Pressure"/>. Операции идут по одной: пока одна идёт или состояние не прочитано, кнопки записи недоступны.
/// </summary>
public sealed partial class LatencyViewModel(TraceRecorder recorder, ISettingsOpener opener, ILogger<LatencyViewModel> logger)
    : ObservableObject
{
    /// <summary>Операция или чтение состояния идёт: обновление при открытии экрана их не перебивает.</summary>
    private bool _running;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissTraceCommand))]
    public partial TraceState State { get; private set; }

    /// <summary>Идёт операция, или состояние ещё не прочитано, или чтение не удалось: неизвестно, идёт ли запись.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissTraceCommand))]
    public partial bool IsBusy { get; private set; } = true;

    /// <summary>Незавершённая запись: идёт или прервана перезагрузкой; null — записи нет.</summary>
    [ObservableProperty]
    public partial string? Banner { get; private set; }

    /// <summary>Итог последней операции или почему запись недоступна.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    /// <summary>Трасса, сохранённая последней остановкой.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealTraceCommand))]
    public partial string? LastTracePath { get; private set; }

    /// <summary>Перечитывает маркер: пока экран был закрыт, компьютер мог перезагрузиться. Во время операции ничего не делает.</summary>
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
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartTraceAsync(CancellationToken ct) => RunAsync("start", async () =>
        Outcome(await Task.Run(() => recorder.StartAsync(ct), ct), Strings.LatencyStarted), ct);

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private Task StopTraceAsync(CancellationToken ct) => RunAsync("stop", async () =>
    {
        var result = await Task.Run(() => recorder.StopAsync(ct), ct);
        if (result.Outcome == WprOutcome.Done)
            LastTracePath = result.Path;
        return Outcome(result, string.Format(CultureInfo.CurrentCulture, Strings.LatencySavedFormat, result.Path));
    }, ct);

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private Task CancelTraceAsync(CancellationToken ct) => RunAsync("cancel", async () =>
        Outcome(await Task.Run(() => recorder.CancelAsync(ct), ct), Strings.LatencyCancelled), ct);

    /// <summary>Прерванная запись: маркер удаляется без UAC, wpr.exe не запускается.</summary>
    [RelayCommand(CanExecute = nameof(IsInterrupted))]
    private Task DismissTraceAsync(CancellationToken ct) => RunAsync("dismiss", async () =>
    {
        await Task.Run(() => recorder.DismissAsync(ct), ct);
        return Strings.LatencyDismissed;
    }, ct);

    [RelayCommand(CanExecute = nameof(HasTrace))]
    private void RevealTrace()
    {
        if (LastTracePath is { } path && !opener.RevealFile(path))
            Status = Strings.LatencyRevealFailed;
    }

    private bool CanStart() => !IsBusy && State == TraceState.Idle;

    private bool IsRecording() => !IsBusy && State == TraceState.Recording;

    private bool IsInterrupted() => !IsBusy && State == TraceState.Interrupted;

    private bool HasTrace() => LastTracePath is not null;

    /// <summary>Операция в пуле, итог — в <see cref="Status"/>; затем состояние перечитывается из маркера, что бы ни случилось.</summary>
    private async Task RunAsync(string operation, Func<Task<string>> run, CancellationToken ct)
    {
        _running = true;
        IsBusy = true;
        try
        {
            Status = await run();
        }
        catch (Exception ex)
        {
            LogOperationFailed(ex, operation);
            Status = string.Format(CultureInfo.CurrentCulture, Strings.LatencyErrorFormat, ex.Message);
        }

        try
        {
            await LoadAsync(ct);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Сбой чтения оставляет кнопки записи недоступными: неизвестно, идёт ли запись.</summary>
    private async Task LoadAsync(CancellationToken ct)
    {
        TraceStatus status;
        try
        {
            status = await Task.Run(() => recorder.GetStatusAsync(ct), ct);
        }
        catch (Exception ex)
        {
            LogStatusFailed(ex);
            Status = string.Format(CultureInfo.CurrentCulture, Strings.LatencyErrorFormat, ex.Message);
            return;
        }

        State = status.State;
        Banner = BannerText(status, CultureInfo.CurrentCulture);
        if (status.State == TraceState.Unavailable)
            Status = Strings.LatencyUnavailable;
        IsBusy = false;
    }

    private static string? BannerText(TraceStatus status, CultureInfo culture) => status switch
    {
        { State: TraceState.Recording, StartedAt: { } startedAt } => string.Format(culture, Strings.LatencyRecordingFormat, Time(startedAt, culture)),
        { State: TraceState.Recording } => Strings.LatencyRecordingUnknown,
        { State: TraceState.Interrupted } => string.Format(culture, Strings.LatencyInterruptedFormat,
            status.StartedAt is { } startedAt ? Time(startedAt, culture) : ReadingFormatter.StatusText(DataStatus.Unknown)),
        _ => null,
    };

    /// <summary>Дата и время: запись могла начаться не сегодня.</summary>
    private static string Time(DateTimeOffset value, CultureInfo culture) => value.ToLocalTime().ToString("g", culture);

    private static string Outcome(WprResult result, string done)
    {
        var culture = CultureInfo.CurrentCulture;
        return result.Outcome switch
        {
            WprOutcome.Done => done,
            WprOutcome.Declined => Strings.LatencyDeclined,
            _ => string.Format(culture, Strings.LatencyFailedFormat, result.ExitCode is { } code
                ? string.Format(culture, Strings.LatencyExitCodeFormat, code.ToString("X8", CultureInfo.InvariantCulture))
                : result.Detail ?? Strings.MaintenanceNoDetail),
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WPR trace {Operation} failed")]
    private partial void LogOperationFailed(Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WPR recording state could not be read")]
    private partial void LogStatusFailed(Exception exception);
}
