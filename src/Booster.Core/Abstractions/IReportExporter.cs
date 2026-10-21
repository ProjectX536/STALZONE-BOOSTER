using Booster.Core.Reports;

namespace Booster.Core.Abstractions;

public interface IReportExporter
{
    string Format { get; }

    Task ExportAsync(SystemReport report, Stream output, CancellationToken ct);
}
