using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Engine;

public sealed partial class SnapshotCollector(IHardwareProvider hardware, IDriverInventoryProvider drivers, IFrameCaptureProvider frameCapture,
    ISensorProvider sensors, SystemDiagnosticsCollector system, IProfileRepository profiles, IPrivilegeInfo privileges,
    ICapabilityRegistry capabilities, SectionRunner runner, IBoosterConfiguration config, TimeProvider time, ILogger<SnapshotCollector> logger)
{
    public const string DriversSectionId = "drivers";

    public const string FrameCaptureSectionId = "frameCapture";

    public const string SensorsSectionId = "sensors";

    public async Task<SystemSnapshot> CollectAsync(CancellationToken ct)
    {
        var takenAt = time.GetUtcNow();
        var privilegeState = privileges.GetCurrent();
        var detectors = config.Current.Settings.Detectors;

        var hardwareTask = hardware.ScanAsync(ct);
        var driversTask = runner.RunAsync(DriversSectionId, drivers.ScanAsync, detectors.TimeoutFor(DriversSectionId), ct);
        var frameCaptureTask = runner.RunAsync(FrameCaptureSectionId, frameCapture.CheckAvailabilityAsync,
            detectors.TimeoutFor(FrameCaptureSectionId), ct);
        var sensorsTask = SurveySensorsAsync(hardwareTask, detectors, ct);
        var systemTask = system.CollectAsync(ct);
        await Task.WhenAll(hardwareTask, driversTask, frameCaptureTask, sensorsTask, systemTask).ConfigureAwait(false);

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
