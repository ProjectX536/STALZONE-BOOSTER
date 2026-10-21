using System.ComponentModel;
using System.Globalization;
using System.IO;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly DiagnosticsSession _session;
    private readonly SessionReport _report;
    private readonly ReportExportService _export;
    private readonly LinkOpener _opener;
    private readonly ILogger<OverviewViewModel> _logger;

    public OverviewViewModel(DiagnosticsSession session, SessionReport report, ReportExportService export, LinkOpener opener,
        ILogger<OverviewViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        _session = session;
        _report = report;
        _export = export;
        _opener = opener;
        _logger = logger;
        _report.PropertyChanged += OnReportChanged;
        Update();
    }

    [ObservableProperty]
    public partial ReportFieldList? Summary { get; private set; }

    [ObservableProperty]
    public partial ReportSection? Findings { get; private set; }

    [ObservableProperty]
    public partial string SnapshotText { get; private set; } = Strings.SnapshotPending;

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken ct)
    {
        StatusMessage = Strings.Collecting;
        try
        {
            await Task.Run(() => _session.RefreshAsync(ct), ct);
            StatusMessage = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusMessage = Strings.RefreshCancelled;
        }
        catch (Exception ex)
        {
            LogRefreshFailed(ex);
            StatusMessage = Strings.RefreshFailed;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken ct)
    {
        StatusMessage = Strings.Exporting;
        try
        {
            var (_, htmlPath) = await Task.Run(() => _export.ExportAsync(null, ct), ct);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Strings.ExportDone, Path.GetDirectoryName(htmlPath));
            _opener.RevealFile(htmlPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusMessage = Strings.ExportCancelled;
        }
        catch (Exception ex)
        {
            LogExportFailed(ex);
            StatusMessage = Strings.ExportFailed;
        }
    }

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionReport.Document))
            Update();
    }

    private void Update()
    {
        if (_report.Report is not { } report || _report.Document is not { } document)
            return;

        Summary = new ReportFieldList(SnapshotSummary.Build(report.Snapshot, CultureInfo.CurrentCulture));
        Findings = document.Section(ReportSectionIds.Findings);
        SnapshotText = string.Format(CultureInfo.CurrentCulture, Strings.SnapshotTakenAt,
            report.Snapshot.TakenAt.ToLocalTime().ToString("G", CultureInfo.CurrentCulture));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "System snapshot refresh failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Report export failed")]
    private partial void LogExportFailed(Exception exception);
}
