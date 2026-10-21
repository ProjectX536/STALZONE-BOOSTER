using Booster.App.Services;
using Booster.Core.Abstractions;
using Booster.Core.Drivers;
using Booster.Core.Hags;
using Booster.Core.Reports;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.TestSupport;

internal sealed class ScreenSession : IDisposable
{
    public ScreenSession(bool collect = false, IStartupSource? startup = null, IGamingSettingsSource? gaming = null, bool elevated = true)
    {
        var profiles = new FakeProfileRepository();
        var collector = collect ? TestCollector.Create(Time, Config, profiles, startup: startup, gaming: gaming, elevated: elevated) : null!;
        var hags = new HagsExperimentService(new InMemoryStateFileStore(), new FakeHagsSource(), new FakeBootTimeSource(), new FakeBenchmarkStore(),
            Config, Time, NullLogger<HagsExperimentService>.Instance);
        Session = new DiagnosticsSession(collector, hags, profiles, Config, Time, NullLogger<DiagnosticsSession>.Instance);
        Report = OnUiContext(() => new SessionReport(Session, new ReportDocumentBuilder(new OfficialSourceResolver(Config), Config)));
    }

    public FakeTimeProvider Time { get; } = new(SessionFactory.Start);

    public FakeConfiguration Config { get; } = new();

    public DiagnosticsSession Session { get; }

    public SessionReport Report { get; }

    public static T OnUiContext<T>(Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose() => Report.Dispose();
}
