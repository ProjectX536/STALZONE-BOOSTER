using Booster.App.Services;
using Booster.App.Tests.TestSupport;
using Booster.Core.Abstractions;
using Booster.Core.Hags;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.Services;

public class DiagnosticsSessionTests
{
    private readonly FakeTimeProvider _time = new(SnapshotFactory.TakenAt);
    private readonly FakeConfiguration _config = new();
    private readonly FakeHagsSource _hags = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticsSession Session(IProfileRepository profiles)
    {
        var collector = TestCollector.Create(_time, _config, profiles,
            new FakePagefileSource(_ => Task.FromResult(new PagefileConfiguration(Reading<bool>.Known(false, "test"), [], []))),
            new FakeStabilityEventSource(new StabilityEvent(StabilityEventKind.ApplicationCrash, SnapshotFactory.TakenAt.AddDays(-1),
                "Application Error", 1000, "cs2.exe")));
        var experiment = new HagsExperimentService(new InMemoryStateFileStore(), _hags, new FakeBootTimeSource(), new FakeBenchmarkStore(), _config,
            _time, NullLogger<HagsExperimentService>.Instance);
        return new DiagnosticsSession(collector, experiment, profiles, _config, _time, NullLogger<DiagnosticsSession>.Instance);
    }

    private static NetworkTestResult SlowGateway() => new(SnapshotFactory.TakenAt, TimeSpan.FromSeconds(30),
        FakeNetworkAdapters.Adapter(gateways: SystemSamples.Gateway),
        [NetworkAnalyzer.Summarize(NetworkTargetKind.Gateway, SystemSamples.Gateway, [30.0, 30.0, 30.0])],
        Reading<double>.Unknown());

    [Fact]
    public void Network_test_is_kept_and_raises_changed()
    {
        var session = Session(new FakeProfileRepository());
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var network = SlowGateway();

        session.SetNetworkTest(network);

        Assert.Same(network, session.Network);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Hags_view_is_kept_and_raises_changed()
    {
        var session = Session(new FakeProfileRepository());
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var view = new HagsExperimentView(null, HagsCheck.NotStarted, Reading<bool>.Known(true, SystemSamples.Source), null, null);

        session.UpdateHags(view);

        Assert.Same(view, session.Hags);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Refresh_reads_hags_experiment_for_report()
    {
        var session = Session(new FakeProfileRepository());

        await session.RefreshAsync(Ct);

        Assert.Equal(HagsCheck.NotStarted, session.Hags?.Check);
        Assert.Same(session.Hags, session.BuildReport().Hags);
    }

    [Fact]
    public async Task Hags_read_failure_keeps_snapshot()
    {
        var session = Session(new FakeProfileRepository());
        session.UpdateHags(new HagsExperimentView(null, HagsCheck.NotStarted, Reading<bool>.Known(true, SystemSamples.Source), null, null));
        _hags.Failure = new IOException("registry unavailable");

        await session.RefreshAsync(Ct);

        Assert.NotNull(session.Snapshot);
        Assert.Null(session.Hags);
        Assert.Null(session.BuildReport().Hags);
    }

    [Fact]
    public async Task Report_contains_system_advice_and_network_test()
    {
        var session = Session(new FakeProfileRepository());
        await session.RefreshAsync(Ct);

        var before = session.BuildReport();
        session.SetNetworkTest(SlowGateway());
        var after = session.BuildReport();

        Assert.Equal([AdviceId.PagefileDisabled], before.Advice.Select(a => a.Id));
        Assert.Null(before.Network);
        Assert.Contains(before.Findings, f => f.Kind == FindingKind.SystemAdvice && f.Subject == EnumText.Format(AdviceId.PagefileDisabled));
        Assert.Equal([AdviceId.PagefileDisabled, AdviceId.NetworkHighLatency], after.Advice.Select(a => a.Id));
        Assert.Same(session.Network, after.Network);
    }

    [Fact]
    public async Task Game_crashes_use_games_from_profiles()
    {
        var session = Session(new FakeProfileRepository(ProfileFactory.Profile(games: ["cs2.exe"])));
        await session.RefreshAsync(Ct);

        var crash = Assert.Single(session.BuildReport().Advice, a => a.Id == AdviceId.GameCrashes);

        Assert.Equal(["cs2.exe", "1"], crash.Arguments);
    }
}
