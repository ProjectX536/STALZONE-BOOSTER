using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Resources;
using Booster.Platform.Windows.Hardware;
using Booster.Providers.Gpu.Generic;
using Booster.Providers.Gpu.Resources;
using Booster.Providers.Gpu.Tests.TestSupport;
using static Booster.Providers.Gpu.Tests.TestSupport.GpuFakes;

namespace Booster.Providers.Gpu.Tests.Generic;

public sealed class GenericGpuProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeGpuCounterSource _counters = new();
    private readonly FakeKmtPerfDataSource _kmt = new();

    private GenericGpuProvider Provider() => new(_counters, _kmt);

    [Fact]
    public async Task Telemetry_combines_counters_and_kmt_by_luid()
    {
        _counters.Next = new() { [Luid] = Counters(42, 3_000_000_000), [OtherLuid] = Counters(7, 1) };
        _kmt.Data[Luid] = Kmt(temperature: 65.3, limit: 83, fan: 1450, memoryClock: 7001);
        _kmt.Data[OtherLuid] = Kmt(temperature: 40, limit: 90, fan: 900, memoryClock: 1000);

        var telemetry = await Provider().ReadTelemetryAsync(Adapter(Luid, vramTotal: 8L << 30), Ct);

        Assert.Equal(42, telemetry.UtilizationPercent.Value);
        Assert.Equal(3_000_000_000, telemetry.VramUsedBytes.Value);
        Assert.Equal(8L << 30, telemetry.VramTotalBytes.Value);
        Assert.Equal(65.3, telemetry.TemperatureC.Value);
        Assert.Equal(83, telemetry.TemperatureLimitC.Value);
        Assert.Equal(1450, telemetry.FanRpm.Value);
        Assert.Equal(7001, telemetry.MemoryClockMhz.Value);
        Assert.Equal([Luid], _kmt.Reads);
    }

    [Fact]
    public async Task Core_clock_power_and_reasons_are_unsupported()
    {
        _counters.Next = new() { [Luid] = Counters(42, 1) };

        var telemetry = await Provider().ReadTelemetryAsync(Adapter(Luid), Ct);

        Assert.Equal(Reading<double>.Unsupported(GenericGpuProvider.Id, ProviderStrings.GenericCoreClockUnavailable), telemetry.CoreClockMhz);
        Assert.Equal(Reading<double>.Unsupported(GenericGpuProvider.Id, ProviderStrings.GenericPowerUnavailable), telemetry.PowerWatts);
        Assert.Equal(Reading<GpuThrottleReasons>.Unsupported(GenericGpuProvider.Id, ProviderStrings.GenericThrottleReasonsVendorOnly),
            telemetry.ThrottleReasons);
    }

    [Fact]
    public async Task Missing_counter_entry_gives_unknown_utilization()
    {
        _counters.Next = new() { [OtherLuid] = Counters(7, 1) };
        _kmt.Data[Luid] = Kmt();

        var telemetry = await Provider().ReadTelemetryAsync(Adapter(Luid), Ct);

        Assert.Equal(DataStatus.Unknown, telemetry.UtilizationPercent.Status);
        Assert.Equal(ProviderStrings.GenericNoCounterEntry, telemetry.UtilizationPercent.Note);
        Assert.Equal(DataStatus.Unknown, telemetry.VramUsedBytes.Status);
        Assert.Equal(ProviderStrings.GenericNoCounterEntry, telemetry.VramUsedBytes.Note);
        Assert.Equal(65.3, telemetry.TemperatureC.Value);
    }

    [Fact]
    public async Task Capabilities_follow_the_probe_read()
    {
        // Первый снимок счётчиков: загрузки ещё нет, но адаптер в счётчиках есть — источник загрузки поддерживается.
        _counters.Next = new() { [Luid] = Counters(null, 1_000) };
        _kmt.Data[Luid] = Kmt(temperature: 50, fan: null, memoryClock: null);

        var capabilities = await Provider().GetCapabilitiesAsync(Adapter(Luid), Ct);

        var scope = CapabilityScopes.Adapter(Luid);
        Assert.Equal(GenericGpuProvider.Id, capabilities.ProviderId);
        Assert.All(capabilities.Capabilities, c =>
        {
            Assert.Equal(scope, c.Scope);
            Assert.Equal(GenericGpuProvider.Id, c.ProviderId);
        });
        var byId = capabilities.Capabilities.ToDictionary(c => c.CapabilityId);
        string[] expected =
        [
            CapabilityIds.GpuUtilization, CapabilityIds.GpuVram, CapabilityIds.GpuTemperature, CapabilityIds.GpuFan,
            CapabilityIds.GpuMemoryClock, CapabilityIds.GpuCoreClock, CapabilityIds.GpuPower, CapabilityIds.GpuThrottleReasonsSource,
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), byId.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(CapabilityStatus.Supported, byId[CapabilityIds.GpuUtilization].Status);
        Assert.Equal(CapabilityStatus.Supported, byId[CapabilityIds.GpuVram].Status);
        Assert.Equal(CapabilityStatus.Supported, byId[CapabilityIds.GpuTemperature].Status);
        Assert.Equal((CapabilityStatus.NotSupported, NotReported), (byId[CapabilityIds.GpuFan].Status, byId[CapabilityIds.GpuFan].Note));
        Assert.Equal((CapabilityStatus.NotSupported, NotReported),
            (byId[CapabilityIds.GpuMemoryClock].Status, byId[CapabilityIds.GpuMemoryClock].Note));
        Assert.Equal((CapabilityStatus.NotSupported, ProviderStrings.GenericCoreClockUnavailable),
            (byId[CapabilityIds.GpuCoreClock].Status, byId[CapabilityIds.GpuCoreClock].Note));
        Assert.Equal((CapabilityStatus.NotSupported, ProviderStrings.GenericPowerUnavailable),
            (byId[CapabilityIds.GpuPower].Status, byId[CapabilityIds.GpuPower].Note));
        Assert.Equal((CapabilityStatus.NotSupported, ProviderStrings.GenericThrottleReasonsVendorOnly),
            (byId[CapabilityIds.GpuThrottleReasonsSource].Status, byId[CapabilityIds.GpuThrottleReasonsSource].Note));
    }

    [Fact]
    public async Task Adapter_missing_from_counters_has_no_utilization_or_vram_capability()
    {
        _counters.Next = new() { [OtherLuid] = Counters(7, 1) };

        var capabilities = await Provider().GetCapabilitiesAsync(Adapter(Luid), Ct);

        var byId = capabilities.Capabilities.ToDictionary(c => c.CapabilityId);
        Assert.Equal((CapabilityStatus.NotSupported, ProviderStrings.GenericNoCounterEntry),
            (byId[CapabilityIds.GpuUtilization].Status, byId[CapabilityIds.GpuUtilization].Note));
        Assert.Equal((CapabilityStatus.NotSupported, ProviderStrings.GenericNoCounterEntry),
            (byId[CapabilityIds.GpuVram].Status, byId[CapabilityIds.GpuVram].Note));
    }

    [Fact]
    public async Task Failed_probe_field_gives_unknown_capability()
    {
        _counters.Next = new() { [Luid] = Counters(1, 1) };
        _kmt.Data[Luid] = KmtPerfDataParser.FromStatus("D3DKMTOpenAdapterFromLuid", unchecked((int)0xC000000D));

        var capabilities = await Provider().GetCapabilitiesAsync(Adapter(Luid), Ct);

        var temperature = capabilities.Capabilities.Single(c => c.CapabilityId == CapabilityIds.GpuTemperature);
        Assert.Equal(CapabilityStatus.Unknown, temperature.Status);
        Assert.Equal("D3DKMTOpenAdapterFromLuid failed: NTSTATUS 0xC000000D", temperature.Note);
    }

    [Fact]
    public async Task Adapter_without_luid_is_not_available()
    {
        var provider = Provider();

        Assert.False(await provider.IsAvailableAsync(Adapter(null), Ct));
        Assert.False(await provider.IsAvailableAsync(Adapter(Luid, isSoftware: true), Ct));
        Assert.True(await provider.IsAvailableAsync(Adapter(Luid), Ct));
        Assert.Empty((await provider.GetCapabilitiesAsync(Adapter(null), Ct)).Capabilities);
        Assert.Equal(DataStatus.Unknown, (await provider.ReadTelemetryAsync(Adapter(null), Ct)).TemperatureC.Status);
        Assert.Empty(_kmt.Reads);
    }

    [Fact]
    public async Task Adapters_of_one_round_share_one_counter_sample()
    {
        _counters.Next = new() { [Luid] = Counters(1, 1), [OtherLuid] = Counters(2, 2) };
        var provider = Provider();

        await provider.GetCapabilitiesAsync(Adapter(Luid), Ct);
        await provider.GetCapabilitiesAsync(Adapter(OtherLuid), Ct);
        await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        await provider.ReadTelemetryAsync(Adapter(OtherLuid), Ct);
        Assert.Equal(1, _counters.Samples);

        await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        await provider.ReadTelemetryAsync(Adapter(OtherLuid), Ct);
        Assert.Equal(2, _counters.Samples);
    }

    [Fact]
    public async Task Settings_are_an_empty_known_list()
    {
        var result = await Provider().ReadSettingsAsync(Adapter(Luid), "game.exe", Ct);

        Assert.Equal(GenericGpuProvider.Id, result.ProviderId);
        Assert.Equal("game.exe", result.Application);
        Assert.Null(result.ProfileName);
        Assert.Equal(DataStatus.Available, result.Settings.Status);
        Assert.Empty(result.Settings.Value!);
    }

    [Fact]
    public async Task Kmt_notes_reach_the_telemetry_unchanged()
    {
        _kmt.Data[Luid] = KmtPerfDataParser.Parse(0, 0, 0, poweredOn: false, caps: null);

        var telemetry = await Provider().ReadTelemetryAsync(Adapter(Luid), Ct);

        Assert.Equal(ReportStrings.KmtAdapterPoweredDown, telemetry.TemperatureC.Note);
        Assert.Equal(ReportStrings.KmtFanZeroRpm, telemetry.FanRpm.Note);
    }
}
