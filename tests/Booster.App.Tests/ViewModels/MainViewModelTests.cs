using Booster.App.Services;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Latency;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void Pages_follow_spec_order()
    {
        using var host = new TempHost();

        var main = host.Resolve<MainViewModel>();

        Assert.Equal([AppPage.Overview, AppPage.Hardware, AppPage.Drivers, AppPage.System, AppPage.Performance, AppPage.Profiles,
            AppPage.Journal, AppPage.Settings], main.Pages.Select(p => p.Page));
        Assert.IsType<SystemViewModel>(main.Pages[3].Content);
    }

    [Fact]
    public void Leaving_a_page_deactivates_it()
    {
        using var host = new TempHost();
        var main = host.Resolve<MainViewModel>();
        var calls = new List<string>();
        var first = new NavigationItem(AppPage.Journal, "first", new RecordingPage("first", calls));
        var second = new NavigationItem(AppPage.Settings, "second", new RecordingPage("second", calls));

        main.SelectedPage = first;
        main.SelectedPage = second;

        Assert.Equal(["first: activate", "first: deactivate", "second: activate"], calls);
    }

    [Fact]
    public async Task Recording_marker_opens_system_page()
    {
        using var host = new TempHost();
        await using var gaming = new FakeGaming();
        var state = new InMemoryStateFileStore();
        state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(FakeBootTimeSource.DefaultBoot.AddMinutes(5)));
        var main = Create(host, gaming, state);

        await main.StartGamingAsync();

        Assert.Equal(AppPage.System, Leave(main));
    }

    [Fact]
    public async Task Interrupted_marker_opens_system_page()
    {
        using var host = new TempHost();
        await using var gaming = new FakeGaming();
        var state = new InMemoryStateFileStore();
        state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(FakeBootTimeSource.DefaultBoot.AddMinutes(-5)));
        var main = Create(host, gaming, state);

        await main.StartGamingAsync();

        Assert.Equal(AppPage.System, Leave(main));
    }

    [Fact]
    public async Task Recovery_report_wins_over_marker()
    {
        using var host = new TempHost();
        await using var gaming = new FakeGaming();
        gaming.Journal.LoadFailure = new IOException("journal is locked");
        var state = new InMemoryStateFileStore();
        state.Put(TraceRecorder.MarkerFileName, TraceMarkers.Json(FakeBootTimeSource.DefaultBoot.AddMinutes(5)));
        var main = Create(host, gaming, state);

        await main.StartGamingAsync();

        Assert.Equal(AppPage.Journal, Leave(main));
    }

    [Fact]
    public async Task Nothing_to_report_keeps_overview()
    {
        using var host = new TempHost();
        await using var gaming = new FakeGaming();
        var main = Create(host, gaming, new InMemoryStateFileStore());

        await main.StartGamingAsync();

        Assert.Equal(AppPage.Overview, Leave(main));
    }

    private static MainViewModel Create(TempHost host, FakeGaming gaming, InMemoryStateFileStore state) => new(
        host.Resolve<StatusBarViewModel>(), host.Resolve<OverviewViewModel>(), host.Resolve<HardwareViewModel>(),
        host.Resolve<DriversViewModel>(), host.Resolve<SystemViewModel>(), host.Resolve<PerformanceViewModel>(),
        host.Resolve<ProfilesViewModel>(), host.Resolve<JournalViewModel>(), host.Resolve<SettingsViewModel>(),
        new GamingLifecycle(gaming.Recovery, gaming.Coordinator, NullLogger<GamingLifecycle>.Instance),
        new TraceRecorder(new FakeWprRunner(), state, new FakeBootTimeSource(), gaming.Time, NullLogger<TraceRecorder>.Instance),
        NullLogger<MainViewModel>.Instance);

    private static AppPage Leave(MainViewModel main)
    {
        var page = main.SelectedPage;
        if (page.Content is IPage active)
            active.Deactivate();
        return page.Page;
    }

    private sealed class RecordingPage(string name, List<string> calls) : IPage
    {
        public void Activate() => calls.Add($"{name}: activate");

        public void Deactivate() => calls.Add($"{name}: deactivate");
    }
}
