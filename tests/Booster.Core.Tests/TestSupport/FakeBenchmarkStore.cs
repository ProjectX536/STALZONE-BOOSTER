using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;

namespace Booster.Core.Tests.TestSupport;

/// <summary>История серий в памяти: те же правила Id и порядка, что у файлового хранилища, без диска.</summary>
public sealed class FakeBenchmarkStore(params BenchmarkSeries[] initial) : IBenchmarkStore
{
    private readonly Lock _lock = new();
    private readonly List<BenchmarkSeries> _series = [.. initial];

    /// <summary>Запись бросает <see cref="IOException"/> — как переполненный диск.</summary>
    public bool FailSave { get; set; }

    /// <summary>Новые первыми.</summary>
    public IReadOnlyList<BenchmarkSeries> Series
    {
        get
        {
            lock (_lock)
                return [.. _series.OrderByDescending(s => s.StartedAt)];
        }
    }

    public Task<BenchmarkSeries> SaveAsync(BenchmarkSeries series, CancellationToken ct)
    {
        if (FailSave)
            throw new IOException("disk full");

        lock (_lock)
        {
            var id = series.Id;
            for (var n = 2; _series.Any(s => s.Id == id); n++)
                id = series.Id + "-" + n.ToString(CultureInfo.InvariantCulture);
            var saved = series with { Id = id };
            _series.Add(saved);
            return Task.FromResult(saved);
        }
    }

    public Task<IReadOnlyList<BenchmarkSeries>> LoadAllAsync(CancellationToken ct) => Task.FromResult(Series);

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        lock (_lock)
            _series.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }
}
