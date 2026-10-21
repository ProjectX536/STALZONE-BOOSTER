using System.Text.Json;
using Booster.Core.Abstractions;

namespace Booster.Core.Reports;

public sealed class JsonReportExporter : IReportExporter
{
    public string Format => "json";

    public Task ExportAsync(SystemReport report, Stream output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);
        return JsonSerializer.SerializeAsync(output, report, ReportJson.Options, ct);
    }
}
