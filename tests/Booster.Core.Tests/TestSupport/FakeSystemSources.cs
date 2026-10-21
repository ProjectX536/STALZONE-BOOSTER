using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeMemoryStatusSource(Func<MemoryStatus>? read = null) : IMemoryStatusSource
{
    public MemoryStatus Read() => read is null ? SystemSamples.Memory() : read();
}

public sealed class FakePagefileSource(Func<CancellationToken, Task<PagefileConfiguration>>? read = null) : IPagefileSource
{
    public Task<PagefileConfiguration> ReadAsync(CancellationToken ct) => read is null ? Task.FromResult(SystemSamples.Pagefile()) : read(ct);
}

public sealed class FakeProcessResourceSource : IProcessResourceSource
{
    private int _ioCalls;

    public int? LastTopCount { get; private set; }

    public System.Collections.Concurrent.ConcurrentQueue<int> MemoryTopCounts { get; } = new();

    public Func<IReadOnlyList<ProcessMemoryUsage>>? Memory { get; set; }

    public Func<int, Reading<ProcessIoActivity>>? Io { get; set; }

    public int FailIoOnCall { get; set; }

    public int IoCalls => Volatile.Read(ref _ioCalls);

    public IReadOnlyList<ProcessMemoryUsage> ReadMemory(int topCount)
    {
        MemoryTopCounts.Enqueue(topCount);
        return [.. (Memory?.Invoke() ?? SystemSamples.Processes).Take(topCount)];
    }

    public Reading<ProcessIoActivity> SampleIo(int topCount)
    {
        var call = Interlocked.Increment(ref _ioCalls);
        LastTopCount = topCount;
        if (call == FailIoOnCall)
            throw new InvalidDataException("process snapshot failed");
        return Io?.Invoke(call) ?? Reading<ProcessIoActivity>.Unknown("fake", "I/O rates need a second reading");
    }
}

public sealed class FakeServiceInventorySource(Func<CancellationToken, Task<IReadOnlyList<ServiceInfo>>>? read = null) : IServiceInventorySource
{
    public Task<IReadOnlyList<ServiceInfo>> ReadAsync(CancellationToken ct) =>
        read is null ? Task.FromResult(SystemSamples.Healthy().Services.Value!) : read(ct);
}

public sealed class FakeStabilityEventSource(params StabilityEvent[] events) : IStabilityEventSource
{
    public DateTimeOffset? LastSince { get; private set; }

    public int? LastMaxEvents { get; private set; }

    public Task<StabilityLog> ReadAsync(DateTimeOffset since, int maxEvents, CancellationToken ct)
    {
        LastSince = since;
        LastMaxEvents = maxEvents;
        return Task.FromResult(new StabilityLog(since, events, Truncated: false));
    }
}

public sealed class FakeStartupSource(params StartupEntry[] entries) : IStartupSource
{
    public Exception? Failure { get; set; }

    public IReadOnlyList<StartupEntry> Read() => Failure is { } failure ? throw failure : entries;
}

public sealed class FakeGamingSettingsSource : IGamingSettingsSource
{
    public GamingSettings Settings { get; set; } =
        new(Reading<bool>.Known(true, SystemSamples.Source), Reading<bool>.Known(false, SystemSamples.Source));

    public Exception? Failure { get; set; }

    public GamingSettings Read() => Failure is { } failure ? throw failure : Settings;
}

public sealed class FakeHagsSource : IHagsSource
{
    public Reading<bool> Enabled { get; set; } = Reading<bool>.Known(true, SystemSamples.Source);

    public Reading<DateTimeOffset> ChangedAt { get; set; } = Reading<DateTimeOffset>.Known(FakeBootTimeSource.DefaultBoot.AddDays(-1), SystemSamples.Source);

    public Exception? Failure { get; set; }

    public ManualResetEventSlim? Block { get; set; }

    public Reading<bool> ReadEnabled()
    {
        Block?.Wait();
        return Failure is { } failure ? throw failure : Enabled;
    }

    public Reading<DateTimeOffset> ReadChangedAt() => Failure is { } failure ? throw failure : ChangedAt;
}
