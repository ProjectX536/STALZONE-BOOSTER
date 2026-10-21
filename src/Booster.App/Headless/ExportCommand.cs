using Booster.App.Services;
using Microsoft.Extensions.Logging;

namespace Booster.App.Headless;

/// <summary>
/// <c>Booster.App.exe --export [папка]</c>: свежий снимок, отчёт в JSON и HTML, выход без окна.
/// Код выхода 0 — оба файла записаны, 1 — ошибка (подробности в журнале). Без папки — папка отчётов Booster.
/// </summary>
public sealed partial class ExportCommand(DiagnosticsSession session, ReportExportService export, ILogger<ExportCommand> logger)
{
    public const string Switch = "--export";

    public const int Success = 0;
    public const int Failure = 1;

    public static bool TryParse(IReadOnlyList<string> args, out string? directory)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], Switch, StringComparison.OrdinalIgnoreCase))
                continue;

            directory = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
            return true;
        }

        directory = null;
        return false;
    }

    public async Task<int> RunAsync(string? directory, CancellationToken ct)
    {
        try
        {
            await session.RefreshAsync(ct).ConfigureAwait(false);
            await export.ExportAsync(directory, ct).ConfigureAwait(false);
            return Success;
        }
        catch (Exception ex)
        {
            LogFailed(ex, directory);
            return Failure;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Headless export to {Directory} failed")]
    private partial void LogFailed(Exception ex, string? directory);
}
