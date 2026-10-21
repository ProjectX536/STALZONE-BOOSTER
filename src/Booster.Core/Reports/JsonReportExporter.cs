using System.Text.Json;
using Booster.Core.Abstractions;

namespace Booster.Core.Reports;

/// <summary>Отчёт целиком в JSON (UTF-8 без BOM) с параметрами <see cref="ReportJson.Options"/>.</summary>
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
