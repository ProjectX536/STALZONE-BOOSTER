using System.ComponentModel;
using System.Globalization;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Booster.App.ViewModels;

public sealed partial class DriversViewModel : ObservableObject
{
    private readonly SessionReport _report;
    private ReportTable? _table;

    public DriversViewModel(SessionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
        _report.PropertyChanged += OnReportChanged;
        Update();
    }

    [ObservableProperty]
    public partial IReadOnlyList<string> Headers { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ReportRow> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial ReportCell? Message { get; private set; }

    [ObservableProperty]
    public partial string? CountText { get; private set; }

    [ObservableProperty]
    public partial bool HasData { get; private set; }

    [ObservableProperty]
    public partial bool ShowProblemsOnly { get; set; }

    partial void OnShowProblemsOnlyChanged(bool value) => Filter();

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionReport.Document))
            Update();
    }

    private void Update()
    {
        if (_report.Document?.Section(ReportSectionIds.Drivers) is not { } section)
            return;

        _table = section.Blocks.OfType<ReportTable>().FirstOrDefault();
        Message = section.Blocks.OfType<ReportParagraph>().FirstOrDefault()?.Content;
        Headers = _table?.Headers ?? [];
        HasData = true;
        Filter();
    }

    private void Filter()
    {
        var all = _table?.Rows ?? [];
        Rows = ShowProblemsOnly ? all.Where(r => r.Flagged).ToList() : all;
        CountText = _table is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.DriversShown, Rows.Count, all.Count);
    }
}
