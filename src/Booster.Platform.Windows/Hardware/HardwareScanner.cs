using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Все секции инвентаризации параллельно, у каждой свой таймаут из settings.json. Сбой или зависание секции
/// дают её статус Failed, остальные собираются как обычно; исключение — только внешняя отмена.
/// </summary>
public sealed class HardwareScanner(
    IInventoryDetector<CpuInventory> cpu,
    IInventoryDetector<GpuInventory> gpu,
    IInventoryDetector<MemoryInventory> memory,
    IInventoryDetector<SystemBoardInfo> board,
    IInventoryDetector<OsInfo> os,
    IInventoryDetector<IReadOnlyList<StorageDeviceInfo>> storage,
    IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>> displays,
    SectionRunner runner,
    IBoosterConfiguration config,
    TimeProvider time) : IHardwareProvider
{
    public async Task<HardwareInventory> ScanAsync(CancellationToken cancellationToken)
    {
        var collectedAt = time.GetUtcNow();
        var settings = config.Current.Settings.Detectors;

        // Детекторы запускаются по очереди, но каждый отдаёт задачу на первом await (WMI и DisplayConfig уходят в пул),
        // поэтому секции идут одновременно, и таймаут каждой отсчитывается с её старта.
        var cpuTask = Run(cpu, settings, cancellationToken);
        var gpuTask = Run(gpu, settings, cancellationToken);
        var memoryTask = Run(memory, settings, cancellationToken);
        var boardTask = Run(board, settings, cancellationToken);
        var osTask = Run(os, settings, cancellationToken);
        var storageTask = Run(storage, settings, cancellationToken);
        var displaysTask = Run(displays, settings, cancellationToken);
        await Task.WhenAll(cpuTask, gpuTask, memoryTask, boardTask, osTask, storageTask, displaysTask).ConfigureAwait(false);

        return new HardwareInventory(collectedAt,
            await cpuTask.ConfigureAwait(false),
            await gpuTask.ConfigureAwait(false),
            await memoryTask.ConfigureAwait(false),
            await boardTask.ConfigureAwait(false),
            await osTask.ConfigureAwait(false),
            await storageTask.ConfigureAwait(false),
            Flatten(await displaysTask.ConfigureAwait(false)));
    }

    private Task<Reading<T>> Run<T>(IInventoryDetector<T> detector, DetectorSettings settings, CancellationToken ct) =>
        runner.RunAsync(detector.SectionId, detector.DetectAsync, settings.TimeoutFor(detector.SectionId), ct);

    /// <summary>
    /// Детектор дисплеев сам возвращает <see cref="Reading{T}"/> («активных путей нет» — Unknown с причиной).
    /// Если секция не собралась (сбой, таймаут), остаётся статус секции.
    /// </summary>
    private static Reading<T> Flatten<T>(Reading<Reading<T>> section) =>
        section.IsAvailable ? section.Value : new Reading<T>(default, section.Status, section.Source, section.Note);
}
