using System.Globalization;
using Booster.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Booster.App.Services;

/// <summary>
/// Отчёт и его раскладка по последнему снимку — общие для всех экранов. Пересобираются при каждом изменении
/// <see cref="DiagnosticsSession"/>; свойства меняются только в UI-потоке, поэтому объект создаётся в нём же.
/// </summary>
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

    /// <summary>Нет, пока не собран первый снимок.</summary>
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
