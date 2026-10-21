using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.SystemHealth;

public class SystemAdvisorTests
{
    private static readonly Thresholds Thresholds = FakeConfiguration.DefaultThresholds;
    private static readonly SystemCatalog Catalog = FakeConfiguration.DefaultCatalog;

    private static SystemSnapshot Troubled()
    {
        var healthy = Healthy();
        return healthy with
        {
            System = healthy.System with
            {
                Pagefile = Known(new PagefileConfiguration(Known(false), [], [])),
                Services = Known<IReadOnlyList<ServiceInfo>>([.. healthy.System.Services.Value!, SystemSamples.Service("VendorUpdater", "Vendor Inc.")]),
                Stability = Known(new StabilityLog(TakenAt.AddDays(-14),
                [
                    new StabilityEvent(StabilityEventKind.BugCheck, TakenAt.AddDays(-1), "Microsoft-Windows-WER-SystemErrorReporting", 1001,
                        "0x0000009f"),
                    new StabilityEvent(StabilityEventKind.ApplicationCrash, TakenAt.AddDays(-2), "Application Error", 1000, "cs2.exe"),
                ], Truncated: false)),
            },
        };
    }

    private static NetworkTestResult SlowGateway() => new(TakenAt, TimeSpan.FromSeconds(30),
        FakeNetworkAdapters.Adapter(gateways: SystemSamples.Gateway),
        [NetworkAnalyzer.Summarize(NetworkTargetKind.Gateway, SystemSamples.Gateway, [30.0, 30.0, 30.0])],
        Reading<double>.Unknown());

    [Fact]
    public void Healthy_system_has_no_advice()
    {
        Assert.Empty(SystemAdvisor.Advise(Healthy(), null, Thresholds, Catalog, ["cs2.exe"], null));
    }

    [Fact]
    public void Advice_is_grouped_by_module_order()
    {
        var advice = SystemAdvisor.Advise(Troubled(), SlowGateway(), Thresholds, Catalog, ["cs2.exe"], null);

        Assert.Equal([AdviceModule.Memory, AdviceModule.Services, AdviceModule.Stability, AdviceModule.Network],
            advice.Select(a => a.Module).Distinct());
        Assert.Equal(advice.Select(a => a.Module).Order(), advice.Select(a => a.Module));
        Assert.Equal([AdviceId.BugChecks, AdviceId.GameCrashes], advice.Where(a => a.Module == AdviceModule.Stability).Select(a => a.Id));
    }

    [Fact]
    public void Network_advice_only_with_test_result()
    {
        Assert.DoesNotContain(SystemAdvisor.Advise(Troubled(), null, Thresholds, Catalog, [], null), a => a.Module == AdviceModule.Network);
        Assert.Contains(SystemAdvisor.Advise(Healthy(), SlowGateway(), Thresholds, Catalog, [], null), a => a.Id == AdviceId.NetworkHighLatency);
    }

    [Fact]
    public void Game_crash_uses_profile_games()
    {
        var withGames = SystemAdvisor.Advise(Troubled(), null, Thresholds, Catalog, ["cs2.exe"], null);
        var withoutGames = SystemAdvisor.Advise(Troubled(), null, Thresholds, Catalog, [], null);

        Assert.Equal(["cs2.exe", "1"], Assert.Single(withGames, a => a.Id == AdviceId.GameCrashes).Arguments);
        Assert.DoesNotContain(withoutGames, a => a.Id == AdviceId.GameCrashes);
    }

    [Fact]
    public void Unavailable_sections_give_no_advice()
    {
        var troubled = Troubled();
        var unavailable = troubled with
        {
            System = troubled.System with
            {
                Pagefile = Reading<PagefileConfiguration>.Failure("pagefile", "WMI broken"),
                Services = Reading<IReadOnlyList<ServiceInfo>>.Failure("services", "WMI broken"),
                Stability = Reading<StabilityLog>.Failure("stability", "log unreadable"),
            },
        };

        Assert.Empty(SystemAdvisor.Advise(unavailable, null, Thresholds, Catalog, ["cs2.exe"], null));
    }

    [Fact]
    public void Startup_and_tweak_advice_follow_module_order()
    {
        var troubled = Troubled();
        var snapshot = troubled with
        {
            System = troubled.System with
            {
                Startup = Known<IReadOnlyList<StartupItem>>([HeavyUserItem("Discord")]),
                Gaming = Known(new GamingSettings(Known(false), Known(true))),
                Overlays = Known<IReadOnlyList<RunningOverlay>>([new RunningOverlay("Discord.exe", "Discord", [4242])]),
            },
        };

        var advice = SystemAdvisor.Advise(snapshot, null, Thresholds, Catalog, ["cs2.exe"], null);

        Assert.Equal([AdviceModule.Memory, AdviceModule.Services, AdviceModule.Stability, AdviceModule.Startup, AdviceModule.Tweaks],
            advice.Select(a => a.Module).Distinct());
        Assert.Equal(advice.Select(a => a.Module).Order(), advice.Select(a => a.Module));
        Assert.Equal([AdviceId.HeavyStartupItems, AdviceId.BackgroundRecordingOn, AdviceId.OverlaysRunning],
            advice.Where(a => a.Module == AdviceModule.Startup).Select(a => a.Id));
        Assert.Equal(["Discord"], Assert.Single(advice, a => a.Id == AdviceId.HeavyStartupItems).Arguments);
        Assert.Equal(AdviceId.GameModeOff, Assert.Single(advice, a => a.Module == AdviceModule.Tweaks).Id);
    }

    [Fact]
    public void Tweak_advice_uses_benchmark_series_of_profile_game()
    {
        const string copyMode = "Composed: Copy with GPU GDI";
        var series = SampleSeries.Full();
        series = series with { Conditions = series.Conditions with { Target = "cs2.exe", PresentModes = [copyMode] } };

        var advice = SystemAdvisor.Advise(Healthy(), null, Thresholds, Catalog, ["cs2.exe"], series);

        var legacy = Assert.Single(advice, a => a.Module == AdviceModule.Tweaks);
        Assert.Equal(AdviceId.LegacyWindowedPresent, legacy.Id);
        Assert.Equal(["cs2.exe", copyMode], legacy.Arguments);
        Assert.Empty(SystemAdvisor.Advise(Healthy(), null, Thresholds, Catalog, ["cs2.exe"], null));
    }

    [Fact]
    public void Latency_advice_uses_benchmark_series()
    {
        var advice = SystemAdvisor.Advise(Healthy(), null, Thresholds, Catalog, ["cs2.exe"], SampleSeries.Full());

        var latency = Assert.Single(advice, a => a.Module == AdviceModule.Latency);
        Assert.Equal(AdviceId.DpcTimeHigh, latency.Id);
        Assert.Equal(AdviceModule.Latency, advice[^1].Module);
        Assert.DoesNotContain(SystemAdvisor.Advise(Healthy(), null, Thresholds, Catalog, ["cs2.exe"], null), a => a.Module == AdviceModule.Latency);
    }

    [Fact]
    public void Unavailable_startup_section_gives_no_advice()
    {
        var healthy = Healthy();
        var unavailable = healthy with
        {
            System = healthy.System with
            {
                Startup = Reading<IReadOnlyList<StartupItem>>.Failure("startup", "registry unreadable"),
                Gaming = Known(new GamingSettings(Reading<bool>.Unknown(Source, "value out of range"), Reading<bool>.Unknown(Source))),
                Overlays = Reading<IReadOnlyList<RunningOverlay>>.Failure("overlays", "process list failed"),
            },
        };

        Assert.Empty(SystemAdvisor.Advise(unavailable, null, Thresholds, Catalog, [], null));
    }

    private static StartupItem HeavyUserItem(string name)
    {
        var path = $@"C:\Users\Player\AppData\Local\{name}\{name}.exe";
        return new StartupItem(new StartupEntry(name, StartupLocation.UserRun, $"\"{path}\"", path, StartupState.Enabled), 300 * (1L << 20));
    }
}
