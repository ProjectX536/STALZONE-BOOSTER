using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Engine;

/// <summary>
/// Снимок системы: оборудование, драйверы и готовность замера кадров собираются параллельно. Перечисление драйверов —
/// самая долгая секция, поэтому у него свой таймаут из settings.json; его сбой или зависание не мешают построить снимок.
/// Датчики опрашиваются после оборудования: им нужен список адаптеров. Системная диагностика собирается параллельно со всем.
/// </summary>
public sealed partial class SnapshotCollector(IHardwareProvider hardware, IDriverInventoryProvider drivers, IFrameCaptureProvider frameCapture,
    ISensorProvider sensors, SystemDiagnosticsCollector system, IProfileRepository profiles, IPrivilegeInfo privileges,
    ICapabilityRegistry capabilities, SectionRunner runner, IBoosterConfiguration config, TimeProvider time, ILogger<SnapshotCollector> logger)
{
    /// <summary>Ключ таймаута драйверов в settings.json и имя секции в логах и отчёте.</summary>
    public const string DriversSectionId = "drivers";

    /// <summary>Имя проверки PresentMon в логах; таймаут — общий для детекторов.</summary>
    public const string FrameCaptureSectionId = "frameCapture";

    /// <summary>Ключ таймаута опроса датчиков и параметров драйвера в settings.json и имя секции в логах и отчёте.</summary>
    public const string SensorsSectionId = "sensors";

    public async Task<SystemSnapshot> CollectAsync(CancellationToken ct)
    {
        var takenAt = time.GetUtcNow();
        var privilegeState = privileges.GetCurrent();
        var detectors = config.Current.Settings.Detectors;

        var hardwareTask = hardware.ScanAsync(ct);
        var driversTask = runner.RunAsync(DriversSectionId, drivers.ScanAsync, detectors.TimeoutFor(DriversSectionId), ct);
        // Результат не нужен: проверка сообщает в реестр возможность monitoring.frame-capture. Без неё отчёт,
        // собранный до открытия экрана производительности (в том числе --export), не знал бы, доступен ли замер кадров.
        var frameCaptureTask = runner.RunAsync(FrameCaptureSectionId, frameCapture.CheckAvailabilityAsync,
            detectors.TimeoutFor(FrameCaptureSectionId), ct);
        var sensorsTask = SurveySensorsAsync(hardwareTask, detectors, ct);
        var systemTask = system.CollectAsync(ct);
        await Task.WhenAll(hardwareTask, driversTask, frameCaptureTask, sensorsTask, systemTask).ConfigureAwait(false);

        // Возможности читаются после сбора: о них сообщают сами детекторы и провайдеры во время сканирования.
        return new SystemSnapshot(takenAt, privilegeState, await hardwareTask.ConfigureAwait(false),
            await driversTask.ConfigureAwait(false), capabilities.All(), await sensorsTask.ConfigureAwait(false),
            await systemTask.ConfigureAwait(false));
    }

    private async Task<Reading<SensorSurvey>> SurveySensorsAsync(Task<HardwareInventory> hardwareTask, DetectorSettings detectors,
        CancellationToken ct)
    {
        var inventory = await hardwareTask.ConfigureAwait(false);
        IReadOnlyList<GpuAdapterInfo> adapters = inventory.Gpu is { IsAvailable: true, Value: { } gpu } ? gpu.Adapters : [];
        var applications = GameApplications();
        return await runner.RunAsync(SensorsSectionId, t => sensors.SurveyAsync(adapters, applications, t),
            detectors.TimeoutFor(SensorsSectionId), ct).ConfigureAwait(false);
    }

    /// <summary>Игры всех профилей: для них читаются параметры драйвера. Профили не загрузились — только глобальные.</summary>
    private IReadOnlyList<string> GameApplications()
    {
        try
        {
            return [.. profiles.Current.Profiles.SelectMany(p => p.Games).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            LogProfilesFailed(ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profiles could not be read; driver settings are read without game profiles")]
    private partial void LogProfilesFailed(Exception ex);
}
