using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Engine;

public class SnapshotCollectorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly CapabilityRegistry _capabilities = new();
    private readonly ListLogger<SectionRunner> _logger = new();

    private sealed class FakeHardware(Func<CancellationToken, Task<HardwareInventory>> scan) : IHardwareProvider
    {
        public Task<HardwareInventory> ScanAsync(CancellationToken cancellationToken) => scan(cancellationToken);
    }

    private sealed class FakeDrivers(Func<CancellationToken, Task<DriverInventoryResult>> scan) : IDriverInventoryProvider
    {
        public Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken) => scan(cancellationToken);
    }

    private sealed class FakePrivileges(PrivilegeState state) : IPrivilegeInfo
    {
        public PrivilegeState GetCurrent() => state;
    }

    private sealed class FakeFrameCapture(Func<CancellationToken, Task<FrameCaptureAvailability>> check) : IFrameCaptureProvider
    {
        public Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => check(cancellationToken);

        public Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private delegate Task<SensorSurvey> Survey(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct);

    private sealed class FakeSensors(Survey survey) : ISensorProvider
    {
        public Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct) =>
            survey(adapters, applications, ct);

        public Task<SensorSnapshot> ReadAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class BrokenProfiles : IProfileRepository
    {
        public ProfileSet Current => throw new IOException("Profiles folder unreadable");

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task SaveUserChoicesAsync(string profileId, ProfileUserChoices choices, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly FrameCaptureAvailability NotInstalled = new(false, null, null, FrameCaptureMode.Direct);

    private static readonly SensorSurvey EmptySurvey = new(new SensorSnapshot(Start, [], SensorSamples.Cpu()), []);

    private static readonly HardwareInventory Inventory = new(Start,
        Reading<CpuInventory>.Unknown(), Reading<GpuInventory>.Unknown(), Reading<MemoryInventory>.Unknown(),
        Reading<SystemBoardInfo>.Unknown(), Reading<OsInfo>.Unknown(), Reading<IReadOnlyList<StorageDeviceInfo>>.Unknown(),
        Reading<IReadOnlyList<DisplayInfo>>.Unknown());

    private readonly ListLogger<SnapshotCollector> _collectorLogger = new();

    private SnapshotCollector Collector(Func<CancellationToken, Task<DriverInventoryResult>>? drivers = null,
        Func<CancellationToken, Task<HardwareInventory>>? hardware = null,
        Func<CancellationToken, Task<FrameCaptureAvailability>>? frameCapture = null,
        Survey? sensors = null,
        IProfileRepository? profiles = null, FakeNetworkAdapters? network = null) => new(
        new FakeHardware(hardware ?? (_ => Task.FromResult(Inventory))),
        new FakeDrivers(drivers ?? (_ => Task.FromResult(new DriverInventoryResult([])))),
        new FakeFrameCapture(frameCapture ?? (_ => Task.FromResult(NotInstalled))),
        new FakeSensors(sensors ?? ((_, _, _) => Task.FromResult(EmptySurvey))),
        new SystemDiagnosticsCollector(new FakeMemoryStatusSource(), new FakePagefileSource(), new FakeProcessResourceSource(),
            new FakeServiceInventorySource(), new FakeStabilityEventSource(), network ?? new FakeNetworkAdapters(), new FakeStartupSource(),
            new FakeGamingSettingsSource(), new FakeHagsSource(), new FakeProcessProvider(),
            new SectionRunner(_time, _logger),
            new FakeConfiguration(), _time),
        profiles ?? new FakeProfileRepository(),
        new FakePrivileges(new PrivilegeState(IsElevated: false, IsPerformanceLogUser: true)),
        _capabilities,
        new SectionRunner(_time, _logger),
        new FakeConfiguration(),
        _time,
        _collectorLogger);

    [Fact]
    public async Task Snapshot_contains_a_sensor_survey()
    {
        var survey = new SensorSurvey(SensorSamples.Snapshot(0, SensorSamples.Telemetry()), []);
        var inventory = Inventory with
        {
            Gpu = Reading<GpuInventory>.Known(new GpuInventory([SensorSamples.Gpu], Reading<bool>.Unknown(),
                Reading<IReadOnlyList<AppGpuPreference>>.Unknown()), "test"),
        };
        var profiles = new FakeProfileRepository(
            ProfileFactory.Profile(games: ["cs2.exe", "valorant.exe"]),
            ProfileFactory.Profile(games: ["CS2.exe", "dota2.exe"], profileId: "custom"));
        IReadOnlyList<GpuAdapterInfo>? polled = null;
        IReadOnlyList<string>? applications = null;

        var snapshot = await Collector(
            hardware: _ => Task.FromResult(inventory),
            sensors: (adapters, apps, _) =>
            {
                polled = adapters;
                applications = apps;
                return Task.FromResult(survey);
            },
            profiles: profiles).CollectAsync(Ct);

        Assert.Equal(DataStatus.Available, snapshot.Sensors.Status);
        Assert.Same(survey, snapshot.Sensors.Value);
        Assert.Equal([SensorSamples.Gpu], polled);
        Assert.Equal(["cs2.exe", "valorant.exe", "dota2.exe"], applications);
    }

    [Fact]
    public async Task Snapshot_contains_system_diagnostics()
    {
        var snapshot = await Collector().CollectAsync(Ct);

        Assert.Equal(SystemSamples.Memory(), snapshot.System.Memory.Value);
        Assert.Equal(DataStatus.Available, snapshot.System.Services.Status);
        Assert.Equal(DataStatus.Available, snapshot.System.Stability.Status);
        Assert.Equal(DataStatus.Available, snapshot.System.NetworkAdapters.Status);
    }

    [Fact]
    public async Task Snapshot_network_adapters_skip_tunnels()
    {
        var network = new FakeNetworkAdapters();
        network.Adapters.Add(FakeNetworkAdapters.Adapter("vpn", NetworkMedium.Tunnel));
        network.Adapters.Add(FakeNetworkAdapters.Adapter("eth0"));

        var snapshot = await Collector(network: network).CollectAsync(Ct);

        Assert.Equal(["eth0"], snapshot.System.NetworkAdapters.Value!.Select(a => a.Id));
    }

    [Fact]
    public async Task Sensor_failure_does_not_fail_the_snapshot()
    {
        var snapshot = await Collector(sensors: (_, _, _) => throw new InvalidOperationException("NVAPI crashed")).CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, snapshot.Sensors.Status);
        Assert.Contains("InvalidOperationException: NVAPI crashed", snapshot.Sensors.Note);
        Assert.Same(Inventory, snapshot.Hardware);
        Assert.True(snapshot.Drivers.IsAvailable);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(SnapshotCollector.SensorsSectionId));
    }

    [Fact]
    public async Task Unknown_gpu_inventory_surveys_no_adapters()
    {
        IReadOnlyList<GpuAdapterInfo>? polled = null;

        await Collector(sensors: (adapters, _, _) =>
        {
            polled = adapters;
            return Task.FromResult(EmptySurvey);
        }).CollectAsync(Ct);

        Assert.NotNull(polled);
        Assert.Empty(polled);
    }

    [Fact]
    public async Task Profile_failure_surveys_no_applications_and_is_logged()
    {
        IReadOnlyList<string>? applications = null;

        var snapshot = await Collector(
            sensors: (_, apps, _) =>
            {
                applications = apps;
                return Task.FromResult(EmptySurvey);
            },
            profiles: new BrokenProfiles()).CollectAsync(Ct);

        Assert.True(snapshot.Sensors.IsAvailable);
        Assert.NotNull(applications);
        Assert.Empty(applications);
        Assert.Contains(_collectorLogger.Entries, e => e.Level == LogLevel.Warning && e.Exception is IOException);
    }

    [Fact]
    public async Task Throwing_driver_scan_fails_only_the_driver_section()
    {
        var snapshot = await Collector(drivers: _ => throw new InvalidOperationException("PnP broken")).CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, snapshot.Drivers.Status);
        Assert.Contains("InvalidOperationException: PnP broken", snapshot.Drivers.Note);
        Assert.Same(Inventory, snapshot.Hardware);
    }

    [Fact]
    public async Task Privileges_and_capabilities_reported_during_the_scan_are_in_the_snapshot()
    {
        var report = new CapabilityReport(CapabilityIds.DxgiAdapters, CapabilityScopes.System, CapabilityStatus.Supported, "test", null);
        var snapshot = await Collector(hardware: _ =>
        {
            _capabilities.Report(report);
            return Task.FromResult(Inventory);
        }).CollectAsync(Ct);

        Assert.True(snapshot.Privileges.IsPerformanceLogUser);
        Assert.False(snapshot.Privileges.IsElevated);
        Assert.Equal(report, Assert.Single(snapshot.Capabilities));
        Assert.True(snapshot.Drivers.IsAvailable);
    }

    [Fact]
    public async Task Frame_capture_readiness_is_in_the_snapshot_without_the_performance_screen()
    {
        var report = new CapabilityReport(CapabilityIds.FrameCapture, CapabilityScopes.System, CapabilityStatus.RequiresAdmin, "test", null);
        var snapshot = await Collector(frameCapture: _ =>
        {
            _capabilities.Report(report);
            return Task.FromResult(new FrameCaptureAvailability(true, @"C:\tools\PresentMon.exe", "2.6.0", FrameCaptureMode.ElevationPrompt));
        }).CollectAsync(Ct);

        Assert.Equal(report, Assert.Single(snapshot.Capabilities));
    }

    [Fact]
    public async Task Throwing_frame_capture_check_is_logged_and_does_not_fail_the_snapshot()
    {
        var snapshot = await Collector(frameCapture: _ => throw new IOException("tools folder unreadable")).CollectAsync(Ct);

        Assert.Same(Inventory, snapshot.Hardware);
        Assert.True(snapshot.Drivers.IsAvailable);
        Assert.Empty(snapshot.Capabilities);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(SnapshotCollector.FrameCaptureSectionId));
    }

    [Fact]
    public async Task Taken_at_comes_from_the_time_provider()
    {
        var snapshot = await Collector().CollectAsync(Ct);

        Assert.Equal(Start, snapshot.TakenAt);
    }

    [Fact]
    public async Task Hardware_and_drivers_run_in_parallel()
    {
        var gate = new TaskCompletionSource();
        var started = new CountdownEvent(2);
        var collector = Collector(
            drivers: async _ =>
            {
                started.Signal();
                await gate.Task;
                return new DriverInventoryResult([]);
            },
            hardware: async _ =>
            {
                started.Signal();
                await gate.Task;
                return Inventory;
            });

        var collect = collector.CollectAsync(Ct);

        Assert.True(started.Wait(TimeSpan.FromSeconds(10), Ct));
        gate.SetResult();
        var snapshot = await collect.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(snapshot.Drivers.IsAvailable);
    }

    [Fact]
    public async Task Driver_scan_uses_the_drivers_timeout_from_settings()
    {
        var hang = new TaskCompletionSource<DriverInventoryResult>();
        var collect = Collector(drivers: _ => hang.Task).CollectAsync(Ct);

        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.False(collect.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(75));
        var snapshot = await collect.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(DataStatus.Failed, snapshot.Drivers.Status);
        Assert.Contains("timeout after 90000 ms", snapshot.Drivers.Note);
    }
}
