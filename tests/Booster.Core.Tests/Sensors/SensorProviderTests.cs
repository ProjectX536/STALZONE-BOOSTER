using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.FakeGpuProvider;

namespace Booster.Core.Tests.Sensors;

public sealed class SensorProviderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly AdapterLuid AmdLuid = new(0x0000D1F0, 0);
    private static readonly GpuAdapterInfo Nvidia = SensorSamples.Gpu;
    private static readonly GpuAdapterInfo Amd = SensorSamples.Adapter("AMD Radeon RX 7800 XT", AmdLuid) with { Vendor = GpuVendor.Amd };
    private static readonly string NvidiaScope = CapabilityScopes.Adapter(SnapshotFactory.GpuLuid);

    private readonly FakeTimeProvider _time = new(SessionFactory.Start);
    private readonly CapabilityRegistry _capabilities = new();
    private readonly FakeCpuSensorSource _cpu = new();
    private readonly ListLogger<SensorProvider> _logger = new();

    private SensorProvider Create(FakeGpuDetector? detector, params IGpuProvider[] providers) =>
        new(detector ?? new FakeGpuDetector(), providers, _cpu, _capabilities, _time, _logger);

    private static GpuTelemetry With(string source, double? temperature = null, double? utilization = null) =>
        GpuTelemetry.AllUnknown(source, null) with
        {
            TemperatureC = temperature is { } t ? Reading<double>.Known(t, source) : Reading<double>.Unknown(source),
            UtilizationPercent = utilization is { } u ? Reading<double>.Known(u, source) : Reading<double>.Unknown(source),
        };

    private static CapabilityReport Report(string id, CapabilityStatus status, string providerId, string? note = null) =>
        new(id, NvidiaScope, status, providerId, note);

    [Fact]
    public async Task Vendor_and_generic_telemetry_are_merged_per_adapter()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia) { Telemetry = _ => With(NvidiaId, temperature: 71) };
        var amd = new FakeGpuProvider(AmdId, GpuVendor.Amd) { Telemetry = _ => With(AmdId, temperature: 65) };
        var generic = new FakeGpuProvider(GenericId) { Telemetry = a => With(GenericId, utilization: a == Nvidia ? 97 : 12) };

        var survey = await Create(null, nvidia, amd, generic).SurveyAsync([Nvidia, Amd], [], Ct);

        var gpus = survey.Sensors.Gpus;
        Assert.Equal([Nvidia, Amd], gpus.Select(g => g.Adapter));
        Assert.Equal([NvidiaId, AmdId], gpus.Select(g => g.ProviderId));
        Assert.Equal(Reading<double>.Known(71, NvidiaId), gpus[0].Telemetry.TemperatureC);
        Assert.Equal(Reading<double>.Known(97, GenericId), gpus[0].Telemetry.UtilizationPercent);
        Assert.Equal(Reading<double>.Known(65, AmdId), gpus[1].Telemetry.TemperatureC);
        Assert.Equal(Reading<double>.Known(12, GenericId), gpus[1].Telemetry.UtilizationPercent);
        Assert.Equal([Nvidia], nvidia.TelemetryReads);
        Assert.Equal([Amd], amd.TelemetryReads);
        Assert.Equal(SessionFactory.Start, survey.Sensors.TakenAt);
        Assert.Same(_cpu.Telemetry, survey.Sensors.Cpu);
    }

    [Fact]
    public async Task Provider_exception_degrades_to_failure_readings()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia) { Failure = new InvalidOperationException("NVAPI_ERROR") };
        var generic = new FakeGpuProvider(GenericId) { Telemetry = _ => With(GenericId, utilization: 50) };

        var snapshot = await Create(new FakeGpuDetector(Nvidia), nvidia, generic).ReadAsync(Ct);

        var gpu = Assert.Single(snapshot.Gpus);
        Assert.Equal(NvidiaId, gpu.ProviderId);
        Assert.Equal(DataStatus.Failed, gpu.Telemetry.TemperatureC.Status);
        Assert.Equal(NvidiaId, gpu.Telemetry.TemperatureC.Source);
        Assert.Contains(nameof(InvalidOperationException), gpu.Telemetry.TemperatureC.Note, StringComparison.Ordinal);
        Assert.Equal(DataStatus.Failed, gpu.Telemetry.ThrottleReasons.Status);
        Assert.Equal(Reading<double>.Known(50, GenericId), gpu.Telemetry.UtilizationPercent);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(NvidiaId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unavailable_vendor_provider_still_reports_its_reason()
    {
        var scope = CapabilityScopes.Adapter(AmdLuid);
        var amd = new FakeGpuProvider(AmdId, GpuVendor.Amd)
        {
            Available = false,
            Capabilities = [new CapabilityReport(CapabilityIds.GpuVendorApi, scope, CapabilityStatus.NotSupported, AmdId, "amd_ags_x64.dll not found")],
        };
        var generic = new FakeGpuProvider(GenericId) { Telemetry = _ => With(GenericId, utilization: 40) };

        var survey = await Create(null, amd, generic).SurveyAsync([Amd], ["cs2.exe"], Ct);

        var report = _capabilities.Get(CapabilityIds.GpuVendorApi, scope);
        Assert.Equal(CapabilityStatus.NotSupported, report.Status);
        Assert.Equal("amd_ags_x64.dll not found", report.Note);
        var gpu = Assert.Single(survey.Sensors.Gpus);
        Assert.Equal(GenericId, gpu.ProviderId);
        Assert.Equal(Reading<double>.Known(40, GenericId), gpu.Telemetry.UtilizationPercent);
        Assert.Empty(amd.TelemetryReads);
        Assert.Empty(amd.SettingsReads);
        Assert.Empty(survey.Settings);
    }

    [Fact]
    public async Task Best_capability_status_wins()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia)
        {
            Capabilities =
            [
                Report(CapabilityIds.GpuTemperature, CapabilityStatus.RequiresAdmin, NvidiaId),
                Report(CapabilityIds.GpuUtilization, CapabilityStatus.NotSupported, NvidiaId),
                Report(CapabilityIds.GpuFan, CapabilityStatus.Supported, NvidiaId),
                Report(CapabilityIds.GpuPower, CapabilityStatus.Unknown, NvidiaId),
            ],
        };
        var generic = new FakeGpuProvider(GenericId)
        {
            Capabilities =
            [
                Report(CapabilityIds.GpuTemperature, CapabilityStatus.NotSupported, GenericId),
                Report(CapabilityIds.GpuUtilization, CapabilityStatus.Supported, GenericId),
                Report(CapabilityIds.GpuFan, CapabilityStatus.Supported, GenericId),
                Report(CapabilityIds.GpuPower, CapabilityStatus.NotSupported, GenericId),
            ],
        };

        await Create(null, nvidia, generic).SurveyAsync([Nvidia], [], Ct);

        Assert.Equal((CapabilityStatus.RequiresAdmin, NvidiaId), Status(CapabilityIds.GpuTemperature));
        Assert.Equal((CapabilityStatus.Supported, GenericId), Status(CapabilityIds.GpuUtilization));
        Assert.Equal((CapabilityStatus.Supported, NvidiaId), Status(CapabilityIds.GpuFan));
        Assert.Equal((CapabilityStatus.NotSupported, GenericId), Status(CapabilityIds.GpuPower));
    }

    [Fact]
    public async Task Cpu_capabilities_are_reported_by_the_survey()
    {
        var report = new CapabilityReport(CapabilityIds.CpuTemperature, CapabilityScopes.System, CapabilityStatus.NotSupported, "cpu", "no source");
        _cpu.Capabilities = [report];

        await Create(null, new FakeGpuProvider(GenericId)).SurveyAsync([], [], Ct);

        Assert.Equal(report, _capabilities.Get(CapabilityIds.CpuTemperature));
    }

    [Fact]
    public async Task Software_adapter_is_not_polled()
    {
        var basic = SensorSamples.Adapter("Microsoft Basic Render Driver", new AdapterLuid(0x0000E000, 0)) with
        {
            Vendor = GpuVendor.Microsoft,
            IsSoftware = true,
        };
        var generic = new FakeGpuProvider(GenericId);

        var survey = await Create(null, generic).SurveyAsync([basic, Nvidia], [], Ct);

        Assert.Equal([Nvidia], survey.Sensors.Gpus.Select(g => g.Adapter));
        Assert.Equal([Nvidia], generic.TelemetryReads);
    }

    [Fact]
    public async Task Adapter_without_luid_is_skipped_and_logged()
    {
        var noLuid = Nvidia with { Name = "Adapter without LUID", Luid = Reading<AdapterLuid>.Unknown() };
        var generic = new FakeGpuProvider(GenericId);

        var survey = await Create(null, generic).SurveyAsync([noLuid, Nvidia], [], Ct);

        Assert.Equal([Nvidia], survey.Sensors.Gpus.Select(g => g.Adapter));
        Assert.Contains(_logger.Entries, e => e.Message.Contains("Adapter without LUID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_without_survey_detects_adapters_once()
    {
        var detector = new FakeGpuDetector(Nvidia);
        var generic = new FakeGpuProvider(GenericId);
        var sensors = Create(detector, generic);

        await sensors.ReadAsync(Ct);
        _time.Advance(TimeSpan.FromSeconds(2));
        var second = await sensors.ReadAsync(Ct);

        Assert.Equal(1, detector.Calls);
        Assert.Equal([Nvidia, Nvidia], generic.TelemetryReads);
        Assert.Equal(SessionFactory.Start.AddSeconds(2), second.TakenAt);
        Assert.Equal(2, _cpu.Samples);
    }

    [Fact]
    public async Task Read_after_survey_uses_the_surveyed_adapters()
    {
        var detector = new FakeGpuDetector(Amd);
        var generic = new FakeGpuProvider(GenericId);
        var sensors = Create(detector, generic);

        await sensors.SurveyAsync([Nvidia], [], Ct);
        var snapshot = await sensors.ReadAsync(Ct);

        Assert.Equal(0, detector.Calls);
        Assert.Equal([Nvidia], snapshot.Gpus.Select(g => g.Adapter));
    }

    [Fact]
    public async Task Global_settings_are_read_once_per_provider()
    {
        var secondNvidia = SensorSamples.Adapter("NVIDIA GeForce RTX 3060", new AdapterLuid(0x0000C2B2, 0));
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia);
        var generic = new FakeGpuProvider(GenericId);

        var survey = await Create(null, nvidia, generic).SurveyAsync([Nvidia, secondNvidia], [], Ct);

        var read = Assert.Single(nvidia.SettingsReads);
        Assert.Equal(Nvidia, read.Adapter);
        Assert.Null(read.Application);
        Assert.Empty(generic.SettingsReads);
        var result = Assert.Single(survey.Settings);
        Assert.Equal(NvidiaId, result.ProviderId);
        Assert.Null(result.Application);
    }

    [Fact]
    public async Task Application_settings_are_read_for_each_game()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia);

        var survey = await Create(null, nvidia, new FakeGpuProvider(GenericId)).SurveyAsync([Nvidia], ["cs2.exe", "valorant.exe"], Ct);

        Assert.Equal([null, "cs2.exe", "valorant.exe"], nvidia.SettingsReads.Select(r => r.Application));
        Assert.Equal([null, "cs2.exe", "valorant.exe"], survey.Settings.Select(s => s.Application));
    }

    [Fact]
    public async Task Settings_and_capability_failures_do_not_stop_the_survey()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia) { Failure = new InvalidOperationException("DRS session") };
        var generic = new FakeGpuProvider(GenericId) { Capabilities = [Report(CapabilityIds.GpuUtilization, CapabilityStatus.Supported, GenericId)] };

        var survey = await Create(null, nvidia, generic).SurveyAsync([Nvidia], ["cs2.exe"], Ct);

        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.GpuUtilization, NvidiaScope).Status);
        Assert.Equal([null, "cs2.exe"], survey.Settings.Select(s => s.Application));
        Assert.All(survey.Settings, s =>
        {
            Assert.Equal(NvidiaId, s.ProviderId);
            Assert.Equal(DataStatus.Failed, s.Settings.Status);
            Assert.Contains(nameof(InvalidOperationException), s.Settings.Note, StringComparison.Ordinal);
        });
        Assert.Equal(DataStatus.Failed, Assert.Single(survey.Sensors.Gpus).Telemetry.TemperatureC.Status);
    }

    [Fact]
    public async Task Availability_check_failure_counts_as_unavailable()
    {
        var nvidia = new FakeGpuProvider(NvidiaId, GpuVendor.Nvidia) { AvailabilityFailure = new DllNotFoundException("nvapi64.dll") };
        var generic = new FakeGpuProvider(GenericId);

        var survey = await Create(null, nvidia, generic).SurveyAsync([Nvidia], [], Ct);

        Assert.Equal(GenericId, Assert.Single(survey.Sensors.Gpus).ProviderId);
        Assert.Empty(nvidia.TelemetryReads);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(NvidiaId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cpu_failure_degrades_to_failure_readings()
    {
        _cpu.Failure = new InvalidOperationException("counter gone");

        var snapshot = await Create(new FakeGpuDetector(), new FakeGpuProvider(GenericId)).ReadAsync(Ct);

        Assert.Equal(DataStatus.Failed, snapshot.Cpu.EffectiveFrequencyMhz.Status);
        Assert.Equal(DataStatus.Failed, snapshot.Cpu.PerformanceLimitPercent.Status);
        Assert.Contains(nameof(InvalidOperationException), snapshot.Cpu.TemperatureC.Note, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Adapter_without_any_provider_has_unknown_telemetry()
    {
        var survey = await Create(null).SurveyAsync([Nvidia], [], Ct);

        var gpu = Assert.Single(survey.Sensors.Gpus);
        Assert.Equal(SensorProvider.NoProviderId, gpu.ProviderId);
        Assert.Equal(DataStatus.Unknown, gpu.Telemetry.TemperatureC.Status);
    }

    [Fact]
    public async Task Concurrent_reads_run_one_at_a_time()
    {
        var gated = new GatedProvider();
        var sensors = Create(new FakeGpuDetector(Nvidia), gated);

        var first = sensors.ReadAsync(Ct);
        var second = sensors.ReadAsync(Ct);
        await gated.Entered.WaitAsync(Ct);
        Assert.False(await gated.Entered.WaitAsync(TimeSpan.FromMilliseconds(100), Ct));
        gated.Release.Release(2);
        await Task.WhenAll(first, second);

        Assert.Equal(1, gated.MaxConcurrent);
    }

    private (CapabilityStatus Status, string ProviderId) Status(string id)
    {
        var report = _capabilities.Get(id, NvidiaScope);
        return (report.Status, report.ProviderId);
    }

    /// <summary>Общий провайдер, чтение телеметрии которого ждёт разрешения теста и считает одновременные вызовы.</summary>
    private sealed class GatedProvider : IGpuProvider
    {
        private int _concurrent;
        private int _maxConcurrent;

        public SemaphoreSlim Entered { get; } = new(0);

        public SemaphoreSlim Release { get; } = new(0);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public string ProviderId => GenericId;

        public GpuVendor? Vendor => null;

        public Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct) => Task.FromResult(true);

        public Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
            Task.FromResult(new GpuCapabilities(GenericId, []));

        public async Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _concurrent);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxConcurrent)) && Interlocked.CompareExchange(ref _maxConcurrent, now, seen) != seen)
            {
            }

            Entered.Release();
            await Release.WaitAsync(ct);
            Interlocked.Decrement(ref _concurrent);
            return GpuTelemetry.AllUnknown(GenericId, null);
        }

        public Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
