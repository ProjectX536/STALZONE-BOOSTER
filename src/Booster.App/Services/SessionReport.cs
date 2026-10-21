using System.Globalization;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Booster.App.Services;

public sealed partial class SessionReport : ObservableObject, IDisposable
{
    private readonly DiagnosticsSession _session;
    private readonly ReportDocumentBuilder _builder;
    private readonly SynchronizationContext _ui;

    public SessionReport(DiagnosticsSession session, ReportDocumentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(builder);
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("SessionReport must be created on the UI thread");
        _session = session;
        _builder = builder;
        _session.Changed += OnSessionChanged;
        Rebuild();
    }

    [ObservableProperty]
    public partial SystemReport? Report { get; private set; }

    [ObservableProperty]
    public partial ReportDocument? Document { get; private set; }

    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged(object? sender, EventArgs e) => _ui.Post(_ => Rebuild(), null);

    private void Rebuild()
    {
        if (_session.Snapshot is null)
            return;

        var report = _session.BuildReport();
        Report = report;
        Document = _builder.Build(report, CultureInfo.CurrentCulture);
    }
}
