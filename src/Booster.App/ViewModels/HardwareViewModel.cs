using System.ComponentModel;
using System.Globalization;
using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public sealed record SensorRow(string Device, string Metric, string Value, string Source);

public sealed partial class HardwareViewModel : ObservableObject, IPage
{
    private static readonly string[] SectionIds =
    [
        ReportSectionIds.Cpu, ReportSectionIds.Gpu, ReportSectionIds.Memory, ReportSectionIds.Board, ReportSectionIds.Os,
        ReportSectionIds.Storage, ReportSectionIds.Displays, ReportSectionIds.Capabilities,
    ];

    private readonly SessionReport _report;
    private readonly ISensorProvider _sensors;
    private readonly IBoosterConfiguration _config;
    private readonly TimeProvider _time;
    private readonly ILogger<HardwareViewModel> _logger;
    private readonly SynchronizationContext? _context;
    private CancellationTokenSource? _loop;

    public HardwareViewModel(SessionReport report, ISensorProvider sensors, IBoosterConfiguration config, TimeProvider time,
        ILogger<HardwareViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
        _sensors = sensors;
        _config = config;
        _time = time;
        _logger = logger;
        _context = SynchronizationContext.Current;
        _report.PropertyChanged += OnReportChanged;
        Update();
    }

    [ObservableProperty]
    public partial IReadOnlyList<ReportSection> Sections { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasData { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<SensorRow> Sensors { get; private set; } = [];

    public void Activate()
    {
        if (_loop is not null)
            return;

        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        _ = Task.Run(() => PollAsync(token), token);
    }

    public void Deactivate()
    {
        if (_loop is null)
            return;

        _loop.Cancel();
        _loop.Dispose();
        _loop = null;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.Current.Settings.Monitoring.SensorIntervalMs), _time);
            do
            {
                try
                {
                    var rows = ToRows(await _sensors.ReadAsync(ct).ConfigureAwait(false));
                    OnUiThread(() => Sensors = rows);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                {
                    LogSensorReadFailed(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Экран закрыт.
        }
    }

    private static List<SensorRow> ToRows(SensorSnapshot snapshot)
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<SensorRow>();
        foreach (var gpu in snapshot.Gpus)
            rows.AddRange(SensorFieldFormatter.Gpu(gpu, culture).Select(f => Row(gpu.Adapter.Name, f)));
        rows.AddRange(SensorFieldFormatter.Cpu(snapshot.Cpu, culture).Select(f => Row(ReportStrings.SectionCpu, f)));
        return rows;
    }

    private static SensorRow Row(string device, (string Label, ReportCell Value) field) =>
        new(device, field.Label, field.Value.Text, field.Value.Tooltip ?? string.Empty);

    private void OnReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionReport.Document))
            Update();
    }

    private void Update()
    {
        if (_report.Document is not { } document)
            return;

        Sections = SectionIds.Select(document.Section).OfType<ReportSection>().ToList();
        HasData = true;
    }

    private void OnUiThread(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(_ => action(), null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live sensor read failed")]
    private partial void LogSensorReadFailed(Exception exception);
}
