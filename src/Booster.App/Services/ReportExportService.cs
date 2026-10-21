using System.Globalization;
using System.IO;
using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

public sealed partial class ReportExportService(DiagnosticsSession session, IEnumerable<IReportExporter> exporters, IAppPaths paths,
    ILogger<ReportExportService> logger)
{
    private const string JsonFormat = "json";
    private const string HtmlFormat = "html";

    // HRESULT_FROM_WIN32(ERROR_FILE_EXISTS)
    private const int ErrorFileExists = unchecked((int)0x80070050);

    private readonly IReadOnlyList<IReportExporter> _exporters = exporters.ToArray();

    public async Task<(string JsonPath, string HtmlPath)> ExportAsync(string? directory, CancellationToken ct)
    {
        var json = Exporter(JsonFormat);
        var html = Exporter(HtmlFormat);

        if (session.Snapshot is null)
            await session.RefreshAsync(ct).ConfigureAwait(false);
        var report = session.BuildReport();

        var target = Path.GetFullPath(directory ?? paths.Reports);
        Directory.CreateDirectory(target);
        var baseName = FreeBaseName(target,
            "booster-report-" + report.Metadata.GeneratedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        var jsonPath = Path.Combine(target, $"{baseName}.{JsonFormat}");
        var htmlPath = Path.Combine(target, $"{baseName}.{HtmlFormat}");
        await WriteAsync(json, report, jsonPath, ct).ConfigureAwait(false);
        try
        {
            await WriteAsync(html, report, htmlPath, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(jsonPath);
            throw;
        }

        LogExported(jsonPath, htmlPath);
        return (jsonPath, htmlPath);
    }

    private IReportExporter Exporter(string format) =>
        _exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No report exporter for format '{format}' is registered");

    private static string FreeBaseName(string directory, string baseName)
    {
        var candidate = baseName;
        for (var i = 2; Taken(candidate); i++)
            candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName}-{i}");
        return candidate;

        bool Taken(string name) =>
            File.Exists(Path.Combine(directory, $"{name}.{JsonFormat}")) || File.Exists(Path.Combine(directory, $"{name}.{HtmlFormat}"));
    }

    private static async Task WriteAsync(IReportExporter exporter, SystemReport report, string path, CancellationToken ct)
    {
        try
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
                await exporter.ExportAsync(report, stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not IOException { HResult: ErrorFileExists })
        {
            TryDelete(path);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Исходная ошибка важнее.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Report exported: {JsonPath}, {HtmlPath}")]
    private partial void LogExported(string jsonPath, string htmlPath);
}
