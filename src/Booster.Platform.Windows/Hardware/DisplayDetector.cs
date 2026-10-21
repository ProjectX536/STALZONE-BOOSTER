using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Дисплеи из активных путей вывода. Результат — сам <see cref="Reading{T}"/>: «активных путей нет» (RDP) —
/// это Unknown с причиной, а не сбой секции, и его нужно донести до отчёта как есть.
/// </summary>
public sealed partial class DisplayDetector(IDisplayConfigSource source, ICapabilityRegistry capabilities, ILogger<DisplayDetector> logger)
    : IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>
{
    public const string ProviderId = "platform.windows.display";

    public string SectionId => SectionIds.Displays;

    public async Task<Reading<IReadOnlyList<DisplayInfo>>> DetectAsync(CancellationToken ct)
    {
        IReadOnlyList<DisplayPathRecord> paths;
        try
        {
            // Вызов синхронный и при смене режима дисплея может надолго зависнуть; в пуле он не держит остальные секции.
            paths = await Task.Run(source.QueryActivePaths, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueryFailed(ex.GetType().Name, ex.Message);
            var note = $"{ex.GetType().Name}: {ex.Message}";
            Report(ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException
                ? CapabilityStatus.NotSupported
                : CapabilityStatus.Unknown, note);
            return Reading<IReadOnlyList<DisplayInfo>>.Failure(DisplayMapper.Source, note);
        }

        Report(CapabilityStatus.Supported, note: null);
        return DisplayMapper.Map(paths);
    }

    private void Report(CapabilityStatus status, string? note) =>
        capabilities.Report(new CapabilityReport(CapabilityIds.DisplayConfig, CapabilityScopes.System, status, ProviderId, note));

    [LoggerMessage(Level = LogLevel.Warning, Message = "QueryDisplayConfig failed: {ErrorType}: {Error}")]
    private partial void LogQueryFailed(string errorType, string error);
}
