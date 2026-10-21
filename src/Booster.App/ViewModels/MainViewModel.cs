using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Latency;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

/// <summary>Разделы окна в порядке навигации.</summary>
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

/// <summary>Экран, которому нужно знать о переходе на него и с него: например, чтобы обновить список процессов.</summary>
public interface IPage
{
    void Activate();

    /// <summary>Уход с экрана: остановить то, что нужно только пока он открыт (например, живые датчики).</summary>
    void Deactivate()
    {
    }
}

/// <summary><see cref="Content"/> — ViewModel экрана; представление подбирается по её типу (DataTemplate в MainWindow).</summary>
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

    /// <summary>
    /// Вызывается, когда окно показано: строка состояния начинает замеры, обзор — первый сбор данных, затем
    /// восстановление незавершённых сессий и наблюдение за играми. Есть что сообщить о восстановлении — открывается журнал,
    /// иначе незавершённая запись трассы WPR открывает «Систему» с напоминанием.
    /// </summary>
    public void Start()
    {
        StatusBar.Start();
        if (_overview.RefreshCommand.CanExecute(null))
            _overview.RefreshCommand.Execute(null);
        _ = StartGamingAsync();
    }

    /// <summary>Журнал важнее трассы: восстановление касается изменений системы, а запись WPR только занимает память.</summary>
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

    /// <summary>Уже открытый экран обновляется, как при переходе на него.</summary>
    private void Open(AppPage page)
    {
        var item = Pages.First(p => p.Page == page);
        if (SelectedPage == item)
            ((IPage)item.Content).Activate();
        else
            SelectedPage = item;
    }

    /// <remarks>При первом выборе <paramref name="oldValue"/> — null, хотя тип свойства его не допускает.</remarks>
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
