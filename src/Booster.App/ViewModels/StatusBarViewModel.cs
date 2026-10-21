using System.Globalization;
using Booster.App.Resources;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Platform.Windows.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly SelfUsageMonitor _monitor;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly ILogger<StatusBarViewModel> _logger;
    private CancellationTokenSource? _stop;

    public StatusBarViewModel(IPrivilegeInfo privileges, SelfUsageMonitor monitor, IBoosterConfiguration config, TimeProvider time,
        ILogger<StatusBarViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        ArgumentNullException.ThrowIfNull(config);
        _monitor = monitor;
        _time = time;
        _logger = logger;
        _interval = TimeSpan.FromMilliseconds(config.Current.Settings.Monitoring.SelfUsageIntervalMs);
        PrivilegesText = FormatPrivileges(privileges.GetCurrent());
        UsageText = Strings.StatusSelfUsagePending;
    }

    public string PrivilegesText { get; }

    [ObservableProperty]
    public partial string UsageText { get; private set; }

    public void Start()
    {
        if (_stop is not null)
            return;

        var ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _stop = new CancellationTokenSource();
        var ct = _stop.Token;
        _ = Task.Run(() => SampleLoopAsync(ui, ct), CancellationToken.None);
    }

    public void Dispose()
    {
        _stop?.Cancel();
        _stop?.Dispose();
    }

    private async Task SampleLoopAsync(SynchronizationContext ui, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval, _time);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var text = FormatUsage(_monitor.Sample());
                ui.Post(_ => UsageText = text, null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogSamplingFailed(ex);
        }
    }

    private static string FormatPrivileges(PrivilegeState state)
    {
        var level = state.IsElevated ? Strings.PrivilegesElevated : Strings.PrivilegesStandard;
        if (state.IsPerformanceLogUser)
            level = string.Format(CultureInfo.CurrentCulture, Strings.PrivilegesWithPerfLogUsers, level);
        return string.Format(CultureInfo.CurrentCulture, Strings.StatusPrivileges, level);
    }

    private static string FormatUsage(SelfUsageSample sample) =>
        string.Format(CultureInfo.CurrentCulture, Strings.StatusSelfUsage,
            string.Format(CultureInfo.CurrentCulture, Strings.PercentFormat, sample.CpuPercent),
            ByteSizeFormatter.Format(sample.WorkingSetBytes, CultureInfo.CurrentCulture));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Self usage sampling stopped")]
    private partial void LogSamplingFailed(Exception ex);
}
