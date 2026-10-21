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

public sealed partial class LatencyViewModel(TraceRecorder recorder, ISettingsOpener opener, ILogger<LatencyViewModel> logger)
    : ObservableObject
{
    private bool _running;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissTraceCommand))]
    public partial TraceState State { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissTraceCommand))]
    public partial bool IsBusy { get; private set; } = true;

    [ObservableProperty]
    public partial string? Banner { get; private set; }

    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealTraceCommand))]
    public partial string? LastTracePath { get; private set; }

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
