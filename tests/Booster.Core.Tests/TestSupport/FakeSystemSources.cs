using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Память системы; по умолчанию — <see cref="SystemSamples.Memory"/>.</summary>
public sealed class FakeMemoryStatusSource(Func<MemoryStatus>? read = null) : IMemoryStatusSource
{
    public MemoryStatus Read() => read is null ? SystemSamples.Memory() : read();
}

/// <summary>Файлы подкачки; по умолчанию — <see cref="SystemSamples.Pagefile"/>.</summary>
public sealed class FakePagefileSource(Func<CancellationToken, Task<PagefileConfiguration>>? read = null) : IPagefileSource
{
    public Task<PagefileConfiguration> ReadAsync(CancellationToken ct) => read is null ? Task.FromResult(SystemSamples.Pagefile()) : read(ct);
}

/// <summary>
/// Процессы из <see cref="Memory"/> (по умолчанию <see cref="SystemSamples.Processes"/>); запоминает запрошенные числа.
/// Ввод-вывод — <see cref="Io"/> по номеру вызова (с 1), по умолчанию Unknown «нужен второй замер»; вызов с номером
/// <see cref="FailIoOnCall"/> бросает.
/// </summary>
public sealed class FakeProcessResourceSource : IProcessResourceSource
{
    private int _ioCalls;

    /// <summary>Число процессов последнего замера ввода-вывода.</summary>
    public int? LastTopCount { get; private set; }

    /// <summary>Числа процессов всех чтений памяти: секции диагностики читают параллельно.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<int> MemoryTopCounts { get; } = new();

    /// <summary>Все процессы снимка; исключение из функции — сбой снимка.</summary>
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

/// <summary>Службы; по умолчанию — три службы Microsoft из <see cref="SystemSamples.Healthy"/>.</summary>
public sealed class FakeServiceInventorySource(Func<CancellationToken, Task<IReadOnlyList<ServiceInfo>>>? read = null) : IServiceInventorySource
{
    public Task<IReadOnlyList<ServiceInfo>> ReadAsync(CancellationToken ct) =>
        read is null ? Task.FromResult(SystemSamples.Healthy().Services.Value!) : read(ct);
}

/// <summary>Журнал стабильности из заданных событий (по умолчанию пустой); запоминает границу и предел чтения.</summary>
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

/// <summary>Автозагрузка из заданных элементов (по умолчанию пусто); <see cref="Failure"/> — чтение бросает.</summary>
public sealed class FakeStartupSource(params StartupEntry[] entries) : IStartupSource
{
    public Exception? Failure { get; set; }

    public IReadOnlyList<StartupEntry> Read() => Failure is { } failure ? throw failure : entries;
}

/// <summary>Game Mode включён, фоновая запись выключена; <see cref="Failure"/> — чтение бросает.</summary>
public sealed class FakeGamingSettingsSource : IGamingSettingsSource
{
    public GamingSettings Settings { get; set; } =
        new(Reading<bool>.Known(true, SystemSamples.Source), Reading<bool>.Known(false, SystemSamples.Source));

    public Exception? Failure { get; set; }

    public GamingSettings Read() => Failure is { } failure ? throw failure : Settings;
}

/// <summary>
/// HAGS включён, ключ записан за сутки до <see cref="FakeBootTimeSource.DefaultBoot"/>; <see cref="Failure"/> — чтение бросает,
/// <see cref="Block"/> — чтение ждёт, пока событие не установят.
/// </summary>
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
