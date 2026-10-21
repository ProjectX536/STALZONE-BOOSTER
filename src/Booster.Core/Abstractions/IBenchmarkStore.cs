using Booster.Core.Benchmark;

namespace Booster.Core.Abstractions;

public interface IBenchmarkStore
{
    Task<BenchmarkSeries> SaveAsync(BenchmarkSeries series, CancellationToken ct);

    Task<IReadOnlyList<BenchmarkSeries>> LoadAllAsync(CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}
