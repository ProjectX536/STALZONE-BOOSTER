using System.Collections.ObjectModel;
using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public sealed record PendingSessionRow(string SessionId, string Title, IReadOnlyList<string> Items)
{
    public override string ToString() => Title;
}

public sealed record JournalSessionRow(SessionRecord Record, string DateText, string KindText, string ProfileName, string GameName,
    string OutcomeText)
{
    public override string ToString() => DateText;
}

public sealed record JournalChangeRow(string Name, string Summary, string StatusText, string? Detail, string? VerifiedText)
{
    public override string ToString() => Name;
}

public sealed record JournalSkippedRow(string Name, string Reason)
{
    public override string ToString() => Name;
}

public sealed partial class JournalViewModel : ObservableObject, IPage
{
    private readonly IChangeJournal _journal;
    private readonly StartupRecovery _recovery;
    private readonly GamingLifecycle _lifecycle;
    private readonly GameSessionCoordinator _coordinator;
    private readonly MaintenanceRunner _maintenance;
    private readonly ILogger<JournalViewModel> _logger;
    private readonly SynchronizationContext? _context;
    private RecoveryReport? _shownReport;
    private string? _lastSessionId;

    public JournalViewModel(IChangeJournal journal, StartupRecovery recovery, GamingLifecycle lifecycle, GameSessionCoordinator coordinator,
        MaintenanceRunner maintenance, ILogger<JournalViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        _journal = journal;
        _recovery = recovery;
        _lifecycle = lifecycle;
        _coordinator = coordinator;
        _maintenance = maintenance;
        _logger = logger;
        _context = SynchronizationContext.Current;
        _lastSessionId = coordinator.Status.LastSession?.SessionId;
        _coordinator.StatusChanged += OnCoordinatorStatusChanged;
    }

    [ObservableProperty]
    public partial IReadOnlyList<string> RecoveredSessions { get; private set; } = [];

    public ObservableCollection<PendingSessionRow> PendingSessions { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Problems { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasRecoveryReport { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<JournalSessionRow> Sessions { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertMaintenanceCommand))]
    public partial JournalSessionRow? SelectedSession { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<JournalChangeRow> SelectedChanges { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<JournalSkippedRow> SelectedSkipped { get; private set; } = [];

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    public void Activate()
    {
        ShowRecoveryReport();
        if (LoadCommand.CanExecute(null))
            LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        JournalContents contents;
        try
        {
            contents = await Task.Run(() => _journal.LoadAsync(ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogLoadFailed(ex);
            StatusMessage = Strings.JournalLoadFailed;
            return;
        }

        var selectedId = SelectedSession?.Record.SessionId;
        Sessions = [.. contents.Sessions.Select(ToRow)];
        SelectedSession = Sessions.FirstOrDefault(s => s.Record.SessionId == selectedId) ?? Sessions.FirstOrDefault();
        StatusMessage = Sessions.Count == 0 ? Strings.JournalEmpty : null;
    }

    [RelayCommand]
    private async Task RecoverAsync(PendingSessionRow? row, CancellationToken ct)
    {
        if (row is null)
            return;

        try
        {
            await Task.Run(() => _recovery.RecoverAsync(row.SessionId, ct), ct);
            PendingSessions.Remove(row);
            StatusMessage = Strings.RecoveryDone;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogRecoverFailed(ex, row.SessionId);
            StatusMessage = Strings.RecoveryFailed;
        }
        await LoadAsync(ct);
    }

    [RelayCommand(CanExecute = nameof(CanRevertMaintenance))]
    private async Task RevertMaintenanceAsync(CancellationToken ct)
    {
        if (SelectedSession?.Record is not { Kind: SessionKind.Maintenance } session)
            return;

        string message;
        try
        {
            await Task.Run(() => _maintenance.RevertAsync(session.SessionId, ct), ct);
            message = Strings.MaintenanceReverted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogRevertFailed(ex, session.SessionId);
            message = Strings.MaintenanceRevertFailed;
        }
        await LoadAsync(ct);
        StatusMessage = message;
    }

    private bool CanRevertMaintenance() => SelectedSession?.Record is { Kind: SessionKind.Maintenance } session
        && session.Changes.Any(c => c.Status is ChangeStatus.Pending or ChangeStatus.Applied or ChangeStatus.RestoreFailed);

    partial void OnSelectedSessionChanged(JournalSessionRow? value)
    {
        SelectedChanges = value is null ? [] : [.. value.Record.Changes.Select(ToRow)];
        SelectedSkipped = value is null ? [] : [.. value.Record.Skipped.Select(s => new JournalSkippedRow(s.DisplayName, s.Reason))];
    }

    private void ShowRecoveryReport()
    {
        var report = _lifecycle.LastRecovery;
        if (report is null || ReferenceEquals(report, _shownReport))
            return;

        _shownReport = report;
        RecoveredSessions = [.. report.Recovered.Select(s => string.Format(CultureInfo.CurrentCulture, Strings.RecoveredSessionFormat,
            Title(s), string.Join("; ", s.Changes.GroupBy(c => c.Status).Select(g => $"{EnumText.Format(g.Key)}: {g.Count()}"))))];
        PendingSessions.Clear();
        foreach (var pending in report.AwaitingUser)
            PendingSessions.Add(new PendingSessionRow(pending.Session.SessionId, Title(pending.Session), [.. pending.Items.Select(ItemText)]));
        Problems = [.. report.Problems.Select(ProblemText)];
        HasRecoveryReport = !report.IsEmpty;
    }

    private static string ProblemText(JournalProblem problem) => problem.Kind == JournalProblemKind.RecoveryFailed
        ? string.Format(CultureInfo.CurrentCulture, Strings.RecoveryFailedProblemFormat, problem.Detail)
        : string.Format(CultureInfo.CurrentCulture, Strings.JournalProblemFormat, problem.FileName, EnumText.Format(problem.Kind), problem.Detail);

    private static string ItemText(RecoveryItem item) => string.Format(CultureInfo.CurrentCulture, Strings.PendingItemFormat,
        item.DisplayName, item.Summary, item.Observed is { } observed ? EnumText.Format(observed) : EnumText.Format(item.Status));

    private static string Title(SessionRecord session) =>
        string.Format(CultureInfo.CurrentCulture, Strings.SessionTitleFormat, Date(session.StartedAt), session.ProfileName, session.GameName);

    private static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static JournalSessionRow ToRow(SessionRecord session) => new(session, Date(session.StartedAt), EnumText.Format(session.Kind),
        session.ProfileName, session.GameName, session.Outcome is { } outcome ? EnumText.Format(outcome) : Strings.SessionNotFinished);

    private static JournalChangeRow ToRow(ChangeEntry change) => new(change.DisplayName, change.Summary, EnumText.Format(change.Status),
        change.Detail, change.Verified switch
        {
            true => Strings.Verified,
            false => Strings.NotVerified,
            null => null,
        });

    private void OnCoordinatorStatusChanged(object? sender, EventArgs e)
    {
        var lastId = _coordinator.Status.LastSession?.SessionId;
        if (lastId is null || lastId == Interlocked.Exchange(ref _lastSessionId, lastId))
            return;

        if (_context is null)
            return;
        _context.Post(_ =>
        {
            if (LoadCommand.CanExecute(null))
                LoadCommand.Execute(null);
        }, null);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Journal load failed")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recovery of session {SessionId} failed")]
    private partial void LogRecoverFailed(Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Revert of maintenance session {SessionId} failed")]
    private partial void LogRevertFailed(Exception exception, string sessionId);
}
