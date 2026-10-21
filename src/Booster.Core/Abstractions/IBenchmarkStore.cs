using Booster.Core.Benchmark;

namespace Booster.Core.Abstractions;

/// <summary>История серий бенчмарка на диске. Id серии — имя файла без расширения.</summary>
public interface IBenchmarkStore
{
    /// <summary>
    /// Атомарная запись; после записи — удаление старых сверх <c>benchmark.retainedSeriesCount</c>.
    /// Файл с таким Id уже есть (две серии одной игры в одну секунду) — к Id добавляется <c>-2</c>, <c>-3</c>…
    /// </summary>
    /// <returns>Сохранённая серия с тем Id, под которым она записана.</returns>
    /// <exception cref="ArgumentException">Id пуст или содержит символы пути.</exception>
    Task<BenchmarkSeries> SaveAsync(BenchmarkSeries series, CancellationToken ct);

    /// <summary>Новые первыми. Нечитаемые файлы и файлы новой схемы пропускаются с записью в лог.</summary>
    Task<IReadOnlyList<BenchmarkSeries>> LoadAllAsync(CancellationToken ct);

    /// <summary>Серии с таким Id нет — ничего не делает.</summary>
    /// <exception cref="ArgumentException">Id пуст или содержит символы пути.</exception>
    Task DeleteAsync(string id, CancellationToken ct);
}
