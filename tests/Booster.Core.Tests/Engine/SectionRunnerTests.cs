using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Engine;

public class SectionRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<SectionRunner> _logger = new();
    private SectionRunner Runner => new(_time, _logger);

    [Fact]
    public async Task Successful_collector_gives_known_value_with_section_as_source()
    {
        var reading = await Runner.RunAsync("cpu", _ => Task.FromResult(42), Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(DataStatus.Available, reading.Status);
        Assert.Equal(42, reading.Value);
        Assert.Equal("cpu", reading.Source);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Throwing_collector_gives_failure_and_one_warning()
    {
        var reading = await Runner.RunAsync<int>("cpu",
            _ => throw new InvalidOperationException("boom"), Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Equal("cpu", reading.Source);
        Assert.Contains("InvalidOperationException: boom", reading.Note);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task Collector_failing_asynchronously_gives_failure()
    {
        // Собственный TimeoutException сборщика — это ошибка сборщика, а не таймаут SectionRunner.
        static async Task<int> Fail(CancellationToken _)
        {
            await Task.Yield();
            throw new TimeoutException("WMI не ответил");
        }

        var reading = await Runner.RunAsync("ram", Fail, Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Contains("TimeoutException: WMI не ответил", reading.Note);
        Assert.DoesNotContain("timeout after", reading.Note);
    }

    [Fact]
    public async Task Collector_that_ignores_token_and_hangs_times_out()
    {
        var never = new TaskCompletionSource<int>();
        CancellationToken seen = default;

        var run = Runner.RunAsync("drivers", ct => { seen = ct; return never.Task; }, Timeout,
            TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted);
        _time.Advance(Timeout);
        var reading = await run;

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Contains("timeout", reading.Note);
        Assert.Contains("15000", reading.Note);
        Assert.True(seen.IsCancellationRequested, "сборщику должен прийти сигнал отмены");
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task External_cancellation_is_propagated()
    {
        using var cts = new CancellationTokenSource();
        var never = new TaskCompletionSource<int>();

        var run = Runner.RunAsync("cpu", _ => never.Task, Timeout, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Collector_returning_null_gives_failure_not_known()
    {
        var reading = await Runner.RunAsync<string>("os", _ => Task.FromResult<string>(null!), Timeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Null(reading.Value);
    }
}
