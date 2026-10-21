using Booster.Core.Reports;

namespace Booster.Core.Abstractions;

/// <summary>Запись отчёта в поток. Поток остаётся открытым: им владеет вызывающий.</summary>
public interface IReportExporter
{
    /// <summary>Расширение файла без точки: <c>json</c>, <c>html</c>.</summary>
    string Format { get; }

    Task ExportAsync(SystemReport report, Stream output, CancellationToken ct);
}
