using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.TestSupport;

/// <summary>
/// Настоящий сборщик снимка на фейках: оборудование из <see cref="SnapshotFactory"/>, драйверов и датчиков нет, права
/// администратора. Подкачку, журнал стабильности, автозагрузку и игровые настройки тест может подменить.
/// </summary>
internal static class TestCollector
{
    public static SnapshotCollector Create(FakeTimeProvider time, FakeConfiguration config, IProfileRepository profiles,
        IPagefileSource? pagefile = null, IStabilityEventSource? stability = null, IStartupSource? startup = null,
        IGamingSettingsSource? gaming = null)
    {
        var runner = new SectionRunner(time, NullLogger<SectionRunner>.Instance);
        var system = new SystemDiagnosticsCollector(new FakeMemoryStatusSource(), pagefile ?? new FakePagefileSource(),
            new FakeProcessResourceSource(), new FakeServiceInventorySource(), stability ?? new FakeStabilityEventSource(),
            new FakeNetworkAdapters(), startup ?? new FakeStartupSource(), gaming ?? new FakeGamingSettingsSource(), new FakeHagsSource(),
            new FakeProcessProvider(), runner, config, time);
        return new SnapshotCollector(new FakeHardware(), new NoDrivers(), new FakeFrameCaptureProvider(time), new NoSensors(), system,
            profiles, new Elevated(), new CapabilityRegistry(), runner, config, time, NullLogger<SnapshotCollector>.Instance);
    }

    private sealed class FakeHardware : IHardwareProvider
    {
        public Task<HardwareInventory> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(SnapshotFactory.Hardware());
    }

    private sealed class NoDrivers : IDriverInventoryProvider
    {
        public Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(new DriverInventoryResult([]));
    }

    private sealed class NoSensors : ISensorProvider
    {
        public Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct) =>
            Task.FromResult(new SensorSurvey(new SensorSnapshot(SnapshotFactory.TakenAt, [], SensorSamples.Cpu()), []));

        public Task<SensorSnapshot> ReadAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Elevated : IPrivilegeInfo
    {
        public PrivilegeState GetCurrent() => new(IsElevated: true, IsPerformanceLogUser: false);
    }
}
