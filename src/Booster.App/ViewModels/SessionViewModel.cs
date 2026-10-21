using System.ComponentModel;
using System.Globalization;
using Booster.App.Resources;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

internal sealed record SessionSensorsView(IReadOnlyList<string> Lines, string? ThrottleText, bool CoolingHint);

public sealed partial class PreviewActionRow : ObservableObject
{
    public PreviewActionRow(PlannedAction planned)
    {
        ArgumentNullException.ThrowIfNull(planned);
        var texts = ActionTexts.For(planned.Action.Id);
        Id = planned.Action.Id;
        Name = planned.Action.Descriptor.DisplayName;
        Summary = planned.Support.Summary;
        Risk = texts.Risk;
        Undo = texts.Undo;
        Reason = planned.WillApply ? null : planned.SkipReason;
        IsPreparation = !planned.Action.Descriptor.IsReversible;
    }

    public string Id { get; }

    public string Name { get; }

    public string Summary { get; }

    public string Risk { get; }

    public string Undo { get; }

    public string? Reason { get; }

    public bool IsPreparation { get; }

    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    public override string ToString() => Name;
}

public sealed record SessionChangeRow(string Name, string Summary, string StatusText)
{
    public override string ToString() => Name;
}

public sealed partial class SessionViewModel : ObservableObject
{
    private readonly GameSessionCoordinator _coordinator;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<SessionViewModel> _logger;
    private readonly SynchronizationContext? _context;

    public SessionViewModel(GameSessionCoordinator coordinator, IProfileRepository profiles, ILogger<SessionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(profiles);
        _coordinator = coordinator;
        _profiles = profiles;
        _logger = logger;
        _context = SynchronizationContext.Current;
        _coordinator.StatusChanged += (_, _) => OnUiThread(ShowStatus);
        _profiles.Changed += (_, _) => OnUiThread(UpdateHasGames);
        ShowStatus();
    }

    [ObservableProperty]
    public partial string? ProfileId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand))]
    public partial bool HasGames { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand), nameof(DisarmCommand), nameof(StopSessionCommand))]
    public partial bool IsArmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand), nameof(DisarmCommand), nameof(StopSessionCommand))]
    public partial bool IsInSession { get; private set; }

    public bool IsIdle => !IsArmed && !IsInSession;

    [ObservableProperty]
    public partial string? ArmedText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand), nameof(ArmCommand))]
    public partial bool IsPreviewing { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCommand), nameof(ArmCommand), nameof(CancelPreviewCommand))]
    public partial bool IsPreparing { get; private set; }

    [ObservableProperty]
    public partial bool HasPreparationConfirmed { get; private set; }

    [ObservableProperty]
    public partial string? PreparationText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<PreviewActionRow> ConfirmActions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PreviewActionRow> AutoActions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PreviewActionRow> SkippedActions { get; private set; } = [];

    [ObservableProperty]
    public partial bool NothingToApply { get; private set; }

    [ObservableProperty]
    public partial string? StateText { get; private set; }

    [ObservableProperty]
    public partial string? GameText { get; private set; }

    [ObservableProperty]
    public partial string? LoadText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> SensorLines { get; private set; } = [];

    [ObservableProperty]
    public partial string? ThrottleText { get; private set; }

    [ObservableProperty]
    public partial bool ShowCoolingHint { get; private set; }

    [ObservableProperty]
    public partial string? FramesText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<SessionChangeRow> Changes { get; private set; } = [];

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    private bool CanPrepare() => ProfileId is not null && HasGames && IsIdle && !IsPreviewing && !IsPreparing;

    [RelayCommand(CanExecute = nameof(CanPrepare))]
    private async Task PrepareAsync(CancellationToken ct)
    {
        if (ProfileId is not { } profileId)
            return;

        StatusMessage = null;
        PreparationText = null;
        SessionPlan plan;
        try
        {
            plan = await Task.Run(() => _coordinator.PreviewAsync(profileId, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogPreviewFailed(ex, profileId);
            StatusMessage = Strings.PreviewFailed;
            return;
        }

        foreach (var row in ConfirmActions)
            row.PropertyChanged -= OnConfirmationChanged;
        ConfirmActions = [.. plan.Actions.Where(IsConfirmable).Select(a => new PreviewActionRow(a))];
        AutoActions = [.. plan.Actions.Where(a => a.WillApply).Select(a => new PreviewActionRow(a))];
        SkippedActions = [.. plan.Actions.Where(a => !a.WillApply && !IsConfirmable(a)).Select(a => new PreviewActionRow(a))];
        foreach (var row in ConfirmActions)
            row.PropertyChanged += OnConfirmationChanged;
        IsPreviewing = true;
        UpdateNothingToApply();
    }

    private bool CanArm() => IsPreviewing && !NothingToApply && !IsPreparing;

    [RelayCommand(CanExecute = nameof(CanArm))]
    private async Task ArmAsync(CancellationToken ct)
    {
        if (ProfileId is not { } profileId)
            return;

        var confirmed = ConfirmActions.Where(a => a.IsConfirmed).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        StatusMessage = null;
        PreparationText = HasPreparationConfirmed ? Strings.PreparationRunning : null;
        IsPreparing = true;
        try
        {
            var result = await Task.Run(() => _coordinator.ArmAsync(profileId, confirmed, ct), ct);
            PreparationText = Describe(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            LogArmFailed(ex, profileId);
            PreparationText = null;
            StatusMessage = Strings.ArmFailed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            PreparationText = null;
        }
        catch (Exception ex)
        {
            LogArmFailed(ex, profileId);
            PreparationText = null;
            StatusMessage = Strings.PreparationFailed;
        }
        finally
        {
            IsPreparing = false;
        }

        ClosePreview();
    }

    private bool CanCancelPreview() => !IsPreparing;

    [RelayCommand(CanExecute = nameof(CanCancelPreview))]
    private void CancelPreview() => ClosePreview();

    private bool CanDisarm() => IsArmed && !IsInSession;

    [RelayCommand(CanExecute = nameof(CanDisarm))]
    private void Disarm() => _coordinator.Disarm();

    private bool CanStopSession() => IsInSession;

    [RelayCommand(CanExecute = nameof(CanStopSession))]
    private void StopSession() => _coordinator.StopSession();

    partial void OnProfileIdChanged(string? value)
    {
        ClosePreview();
        UpdateHasGames();
    }

    private static bool IsConfirmable(PlannedAction planned) =>
        planned.Support.IsSupported && planned.Decision.Verdict == PolicyVerdict.RequireConfirmation;

    private void OnConfirmationChanged(object? sender, PropertyChangedEventArgs e) => UpdateNothingToApply();

    private void UpdateNothingToApply()
    {
        NothingToApply = IsPreviewing && AutoActions.Count == 0 && !ConfirmActions.Any(a => a.IsConfirmed);
        HasPreparationConfirmed = ConfirmActions.Any(a => a.IsConfirmed && a.IsPreparation);
        ArmCommand.NotifyCanExecuteChanged();
    }

    private static string? Describe(PreparationResult result)
    {
        if (result.SkippedReason is { } reason)
            return string.Format(CultureInfo.CurrentCulture, Strings.PreparationSkippedFormat, reason);
        if (result.Preparation is not { } session)
            return null;

        var lines = session.Changes.Select(c => Line(c.DisplayName, c.Detail ?? EnumText.Format(c.Status)))
            .Concat(session.Skipped.Select(s => Line(s.DisplayName, s.Reason)));
        return string.Join(Environment.NewLine, lines.Prepend(Strings.PreparationHeader));
    }

    private static string Line(string name, string text) => string.Format(CultureInfo.CurrentCulture, Strings.PreparationLineFormat, name, text);

    private void ClosePreview()
    {
        foreach (var row in ConfirmActions)
            row.PropertyChanged -= OnConfirmationChanged;
        ConfirmActions = [];
        AutoActions = [];
        SkippedActions = [];
        IsPreviewing = false;
        UpdateNothingToApply();
    }

    private void UpdateHasGames() =>
        HasGames = ProfileId is { } id && _profiles.Current.Find(id) is { Games.Count: > 0 };

    private void ShowStatus()
    {
        var status = _coordinator.Status;
        IsInSession = status.Mode == CoordinatorMode.InSession;
        IsArmed = !IsInSession && status.ArmedProfileId is not null;
        ArmedText = status.ArmedProfileId is { } armedId
            ? string.Format(CultureInfo.CurrentCulture, Strings.ArmedFormat, _profiles.Current.Find(armedId)?.Name ?? armedId)
            : null;

        var progress = status.Progress;
        StateText = progress is null ? null : EnumText.Format(progress.State);
        GameText = status.Plan?.Game?.Name ?? progress?.Session?.GameName;
        LoadText = progress?.Load is { } load
            ? string.Format(CultureInfo.CurrentCulture, Strings.SessionLoadFormat, Percent(load.CpuPercent), Percent(load.MemoryLoadPercent))
            : null;
        var sensors = DescribeSensors(progress, status.Plan?.Profile.Monitoring.CollectCpuMetrics ?? false);
        SensorLines = sensors.Lines;
        ThrottleText = sensors.ThrottleText;
        ShowCoolingHint = sensors.CoolingHint;
        FramesText = DescribeFrames(progress?.Frames);
        Changes = progress?.Session is { } session ? [.. session.Changes.Select(ToRow)] : [];
        if (IsInSession && IsPreviewing)
            ClosePreview();
    }

    internal static SessionSensorsView DescribeSensors(SessionProgress? progress, bool collectCpu)
    {
        if (progress?.Sensors is not { } snapshot)
            return new SessionSensorsView([], null, false);

        var culture = CultureInfo.CurrentCulture;
        var lines = new List<string>();
        foreach (var gpu in snapshot.Gpus)
        {
            var fields = SensorFieldFormatter.Gpu(gpu, culture).ToDictionary(f => f.Label, f => f.Value.Text, StringComparer.Ordinal);
            lines.Add(string.Format(culture, Strings.SessionGpuFormat, gpu.Adapter.Name, fields[ReportStrings.SensorUtilization],
                fields[ReportStrings.SensorTemperature], fields[ReportStrings.SensorCoreClock]));
        }

        if (collectCpu)
        {
            var fields = SensorFieldFormatter.Cpu(snapshot.Cpu, culture).ToDictionary(f => f.Label, f => f.Value.Text, StringComparer.Ordinal);
            lines.Add(string.Format(culture, Strings.SessionCpuFormat, ReportStrings.SectionCpu, fields[ReportStrings.CpuEffectiveFrequency],
                fields[ReportStrings.CpuPerformanceLimit]));
        }

        if (progress.Throttle is not { } throttle)
            return new SessionSensorsView(lines, null, false);

        var signals = throttle.Findings.Select(f => f.Signal).Distinct().Select(s => EnumText.Format(s)).ToList();
        return signals.Count == 0
            ? new SessionSensorsView(lines, Strings.SessionNoThrottle, false)
            : new SessionSensorsView(lines, string.Format(culture, Strings.SessionThrottleFormat, string.Join(ReportStrings.ListSeparator, signals)), true);
    }

    internal static string? DescribeFrames(SessionFrameSummary? frames)
    {
        var culture = CultureInfo.CurrentCulture;
        return frames switch
        {
            null or { Status: SessionFrameStatus.Disabled } => null,
            { Status: SessionFrameStatus.Capturing } => string.Format(culture, Strings.SessionFramesCapturingFormat, Length(frames.Duration)),
            { Status: SessionFrameStatus.NeedsElevation } => Strings.SessionFramesNeedsElevation,
            { Status: SessionFrameStatus.NotInstalled } => Strings.SessionFramesNotInstalled,
            { Status: SessionFrameStatus.Completed, Metrics.Metrics: { } metrics } => string.Format(culture, Strings.SessionFramesFormat,
                Length(TimeSpan.FromMilliseconds(metrics.DurationMs)), Number(metrics.AverageFps), Number(metrics.OnePercentLowFps),
                Number(frames.Stutter?.StuttersPerMinute ?? 0)),
            { Status: SessionFrameStatus.Completed, Metrics: { } result } => string.Format(culture, Strings.SessionFramesTooFewFormat,
                result.ValidIntervals),
            _ => Strings.SessionFramesFailed,
        };
    }

    private static string Length(TimeSpan duration)
    {
        var culture = CultureInfo.CurrentCulture;
        return duration.TotalSeconds >= 60
            ? string.Format(culture, Strings.SessionFramesMinutesFormat, (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero))
            : string.Format(culture, Strings.SessionFramesSecondsFormat, (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
    }

    private static string Number(double value) => value.ToString("0.0", CultureInfo.CurrentCulture);

    private static SessionChangeRow ToRow(ChangeEntry change) => new(change.DisplayName, change.Summary, EnumText.Format(change.Status));

    private static string Percent(double? value) =>
        value is { } v ? string.Format(CultureInfo.CurrentCulture, Strings.PercentFormat, v) : Strings.NoData;

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(_ => action(), null);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Session preview for profile {ProfileId} failed")]
    private partial void LogPreviewFailed(Exception exception, string profileId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arming or preparing profile {ProfileId} failed")]
    private partial void LogArmFailed(Exception exception, string profileId);
}
