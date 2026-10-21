using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Latency;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

public enum AppPage
{
    Overview,
    Hardware,
    Drivers,
    System,
    Performance,
    Profiles,
    Journal,
    Settings,
}

public interface IPage
{
    void Activate();

    void Deactivate()
    {
    }
}

public sealed record NavigationItem(AppPage Page, string Title, object Content);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly OverviewViewModel _overview;
    private readonly GamingLifecycle _gaming;
    private readonly TraceRecorder _traces;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(StatusBarViewModel statusBar, OverviewViewModel overview, HardwareViewModel hardware, DriversViewModel drivers,
        SystemViewModel system, PerformanceViewModel performance, ProfilesViewModel profiles, JournalViewModel journal, SettingsViewModel settings,
        GamingLifecycle gaming, TraceRecorder traces, ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(statusBar);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(gaming);
        ArgumentNullException.ThrowIfNull(traces);
        StatusBar = statusBar;
        _overview = overview;
        _gaming = gaming;
        _traces = traces;
        _logger = logger;
        Pages =
        [
            new(AppPage.Overview, Strings.NavOverview, overview),
            new(AppPage.Hardware, Strings.NavHardware, hardware),
            new(AppPage.Drivers, Strings.NavDrivers, drivers),
            new(AppPage.System, Strings.NavSystem, system),
            new(AppPage.Performance, Strings.NavPerformance, performance),
            new(AppPage.Profiles, Strings.NavProfiles, profiles),
            new(AppPage.Journal, Strings.NavJournal, journal),
            new(AppPage.Settings, Strings.NavSettings, settings),
        ];
        SelectedPage = Pages[0];
    }

    public IReadOnlyList<NavigationItem> Pages { get; }

    [ObservableProperty]
    public partial NavigationItem SelectedPage { get; set; }

    public StatusBarViewModel StatusBar { get; }

    public void Start()
    {
        StatusBar.Start();
        if (_overview.RefreshCommand.CanExecute(null))
            _overview.RefreshCommand.Execute(null);
        _ = StartGamingAsync();
    }

    internal async Task StartGamingAsync()
    {
        var report = await _gaming.StartAsync();
        if (!report.IsEmpty)
            Open(AppPage.Journal);
        else if (await HasUnfinishedTraceAsync())
            Open(AppPage.System);
    }

    private async Task<bool> HasUnfinishedTraceAsync()
    {
        try
        {
            var status = await _traces.GetStatusAsync(CancellationToken.None);
            return status.State is TraceState.Recording or TraceState.Interrupted;
        }
        catch (Exception ex)
        {
            LogTraceStatusFailed(ex);
            return false;
        }
    }

    private void Open(AppPage page)
    {
        var item = Pages.First(p => p.Page == page);
        if (SelectedPage == item)
            ((IPage)item.Content).Activate();
        else
            SelectedPage = item;
    }

    partial void OnSelectedPageChanged(NavigationItem oldValue, NavigationItem newValue)
    {
        if (oldValue?.Content is IPage left)
            left.Deactivate();
        if (newValue?.Content is IPage page)
            page.Activate();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WPR recording state could not be read at startup")]
    private partial void LogTraceStatusFailed(Exception exception);
}
