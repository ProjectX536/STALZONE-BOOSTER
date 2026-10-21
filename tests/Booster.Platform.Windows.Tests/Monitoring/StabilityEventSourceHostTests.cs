using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>Читают журналы System и Application живой системы, ничего не меняя.</summary>
[Trait("Category", "Host")]
public class StabilityEventSourceHostTests
{
    [Fact]
    public async Task Reads_last_day_without_error()
    {
        var since = DateTimeOffset.Now.AddDays(-1);

        var log = await new StabilityEventSource().ReadAsync(since, 50, TestContext.Current.CancellationToken);

        Assert.Equal(since, log.Since);
        Assert.True(log.Events.Count <= 50);
        Assert.All(log.Events, e => Assert.True(e.Time >= since.AddSeconds(-1)));
    }

    [Fact]
    public async Task Reader_stops_at_max_events_and_marks_truncated()
    {
        var since = DateTimeOffset.Now.AddDays(-30);
        var source = new StabilityEventSource();
        var all = await source.ReadAsync(since, 1000, TestContext.Current.CancellationToken);

        var one = await source.ReadAsync(since, 1, TestContext.Current.CancellationToken);

        if (all.Events.Count >= 2)
        {
            Assert.True(one.Truncated);
            Assert.Single(one.Events);
            // Самое новое; за время между чтениями могло появиться ещё более новое.
            Assert.True(one.Events[0].Time >= all.Events.Max(e => e.Time));
        }
        else
        {
            Assert.True(one.Events.Count <= 1);
        }
    }
}
