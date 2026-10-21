using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Providers.Gpu.Nvidia;
using Booster.Providers.Gpu.Resources;
using Booster.Providers.Gpu.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static Booster.Providers.Gpu.Tests.TestSupport.GpuFakes;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvidiaProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeNvApiFactory _factory = new();

    private NvidiaProvider Provider() => new(_factory, NullLogger<NvidiaProvider>.Instance);

    private FakeNvApi Api(params NvPhysicalGpu[] gpus)
    {
        var api = new FakeNvApi();
        api.Gpus.AddRange(gpus);
        _factory.Instances.Enqueue(api);
        return api;
    }

    [Fact]
    public async Task Missing_nvapi_reports_vendor_api_not_supported_with_code()
    {
        var provider = Provider();

        Assert.False(await provider.IsAvailableAsync(Adapter(Luid), Ct));
        var capabilities = await provider.GetCapabilitiesAsync(Adapter(Luid), Ct);

        var vendorApi = Assert.Single(capabilities.Capabilities, c => c.CapabilityId == CapabilityIds.GpuVendorApi);
        Assert.Equal(CapabilityStatus.NotSupported, vendorApi.Status);
        Assert.Contains("NVAPI_LIBRARY_NOT_FOUND", vendorApi.Note, StringComparison.Ordinal);
        Assert.Equal(1, _factory.Created);
    }

    [Fact]
    public async Task Vendor_api_names_the_driver_version()
    {
        Api(NvFakes.Gpu(1, Luid));

        var capabilities = await Provider().GetCapabilitiesAsync(Adapter(Luid), Ct);

        var vendorApi = Assert.Single(capabilities.Capabilities, c => c.CapabilityId == CapabilityIds.GpuVendorApi);
        Assert.Equal(CapabilityStatus.Supported, vendorApi.Status);
        Assert.Equal("NVAPI, драйвер 560.94", vendorApi.Note);
        Assert.Equal(CapabilityStatus.Supported,
            Assert.Single(capabilities.Capabilities, c => c.CapabilityId == CapabilityIds.GpuSettingsRead && c.Scope == CapabilityScopes.System).Status);
        Assert.Equal(CapabilityStatus.Supported, capabilities.Capabilities.Single(c => c.CapabilityId == CapabilityIds.GpuThrottleReasonsSource).Status);
    }

    [Fact]
    public async Task Adapters_are_matched_by_luid_not_by_order()
    {
        var api = Api(NvFakes.Gpu(2, OtherLuid), NvFakes.Gpu(1, Luid));
        api.Temperature[1] = 63;
        api.Temperature[2] = 40;
        var provider = Provider();

        Assert.Equal(63, (await provider.ReadTelemetryAsync(Adapter(Luid), Ct)).TemperatureC.Value);
        Assert.Equal(40, (await provider.ReadTelemetryAsync(Adapter(OtherLuid), Ct)).TemperatureC.Value);
    }

    [Fact]
    public async Task Pci_fallback_only_when_unambiguous()
    {
        var api = Api(NvFakes.Gpu(1, null, NvFakes.Pci));
        api.Temperature[1] = 63;

        var provider = Provider();
        Assert.True(await provider.IsAvailableAsync(Adapter(Luid).WithPci(NvFakes.Pci), Ct));
        Assert.Equal(63, (await provider.ReadTelemetryAsync(Adapter(Luid).WithPci(NvFakes.Pci), Ct)).TemperatureC.Value);

        Api(NvFakes.Gpu(1, null, NvFakes.Pci), NvFakes.Gpu(2, null, NvFakes.Pci));
        var ambiguous = new NvidiaProvider(_factory, NullLogger<NvidiaProvider>.Instance);
        Assert.False(await ambiguous.IsAvailableAsync(Adapter(Luid).WithPci(NvFakes.Pci), Ct));
        var telemetry = await ambiguous.ReadTelemetryAsync(Adapter(Luid).WithPci(NvFakes.Pci), Ct);
        Assert.Equal(DataStatus.Unknown, telemetry.TemperatureC.Status);
        Assert.Equal(ProviderStrings.NvidiaNoAdapterMatch, telemetry.TemperatureC.Note);
    }

    [Fact]
    public async Task Gpu_with_known_luid_is_not_matched_by_pci()
    {
        Api(NvFakes.Gpu(1, OtherLuid, NvFakes.Pci));

        Assert.False(await Provider().IsAvailableAsync(Adapter(Luid).WithPci(NvFakes.Pci), Ct));
    }

    [Fact]
    public async Task Invalidated_handle_recreates_the_library()
    {
        var first = Api(NvFakes.Gpu(1, Luid));
        first.SensorStatus = NvApiStatus.HandleInvalidated;
        var second = Api(NvFakes.Gpu(7, Luid));
        second.Temperature[7] = 55;
        var provider = Provider();

        var failed = await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        Assert.Equal(DataStatus.Failed, failed.TemperatureC.Status);
        Assert.True(first.Disposed);

        var recovered = await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        Assert.Equal(55, recovered.TemperatureC.Value);
        Assert.Equal(2, _factory.Created);
    }

    [Fact]
    public async Task Failed_creation_is_not_retried()
    {
        var provider = Provider();

        await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        await provider.ReadTelemetryAsync(Adapter(Luid), Ct);

        Assert.Equal(1, _factory.Created);
    }

    [Fact]
    public async Task Limit_vram_and_power_are_left_to_the_generic_provider()
    {
        Api(NvFakes.Gpu(1, Luid));

        var telemetry = await Provider().ReadTelemetryAsync(Adapter(Luid, vramTotal: 8L << 30), Ct);

        Assert.Equal(Reading<double>.Unknown("nvapi"), telemetry.TemperatureLimitC);
        Assert.Equal(Reading<long>.Unknown("nvapi"), telemetry.VramUsedBytes);
        Assert.Equal(Reading<long>.Unknown("nvapi"), telemetry.VramTotalBytes);
        Assert.Equal(Reading<double>.Unknown("nvapi"), telemetry.PowerWatts);
        Assert.Equal(1500, telemetry.CoreClockMhz.Value);
    }

    [Fact]
    public async Task Application_without_profile_has_null_profile_name()
    {
        var api = Api(NvFakes.Gpu(1, Luid));
        api.ProfileName = null;

        var result = await Provider().ReadSettingsAsync(Adapter(Luid), "game.exe", Ct);

        Assert.Equal(NvidiaProvider.Id, result.ProviderId);
        Assert.Equal("game.exe", result.Application);
        Assert.Null(result.ProfileName);
        Assert.Empty(result.Settings.Value!);
        Assert.Equal(["game.exe"], api.DrsApplications);
    }

    [Fact]
    public async Task Global_settings_list_the_whole_catalog()
    {
        var api = Api(NvFakes.Gpu(1, Luid));
        api.DrsValues[0x1057EB71] = new(0x1057EB71, 1, false, 0);

        var result = await Provider().ReadSettingsAsync(Adapter(Luid), null, Ct);

        Assert.Equal("Base Profile", result.ProfileName);
        Assert.Equal(NvDrsSettingCatalog.SettingIds.Count, result.Settings.Value!.Count);
        Assert.Equal(GpuSettingOrigin.UserChanged, result.Settings.Value.Single(s => s.Id == "0x1057EB71").Origin);
        Assert.All(result.Settings.Value.Where(s => s.Id != "0x1057EB71"), s => Assert.Equal(GpuSettingOrigin.NotSet, s.Origin));
    }

    [Fact]
    public async Task Application_profile_shows_only_found_settings()
    {
        var api = Api(NvFakes.Gpu(1, Luid));
        api.ProfileName = "Game";
        api.DrsValues[0x10835002] = new(0x10835002, 141, false, 0);

        var result = await Provider().ReadSettingsAsync(Adapter(Luid), "game.exe", Ct);

        Assert.Equal("Game", result.ProfileName);
        Assert.Equal("141 кадр/с", Assert.Single(result.Settings.Value!).CurrentValue.Value);
    }

    [Fact]
    public async Task Failed_drs_read_gives_settings_status_and_capability()
    {
        var api = Api(NvFakes.Gpu(1, Luid));
        api.DrsStatus = NvApiStatus.InvalidUserPrivilege;
        var provider = Provider();

        var result = await provider.ReadSettingsAsync(Adapter(Luid), null, Ct);
        var capabilities = await provider.GetCapabilitiesAsync(Adapter(Luid), Ct);

        Assert.Equal(DataStatus.RequiresAdmin, result.Settings.Status);
        var settingsRead = capabilities.Capabilities.Single(c => c.CapabilityId == CapabilityIds.GpuSettingsRead);
        Assert.Equal(CapabilityStatus.NotSupported, settingsRead.Status);
        Assert.Contains("NVAPI_INVALID_USER_PRIVILEGE", settingsRead.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_never_throws_from_read()
    {
        var api = Api(NvFakes.Gpu(1, Luid));
        var provider = Provider();
        Assert.True(await provider.IsAvailableAsync(Adapter(Luid), Ct));
        api.Throw = new InvalidOperationException("boom");

        var telemetry = await provider.ReadTelemetryAsync(Adapter(Luid), Ct);
        var settings = await provider.ReadSettingsAsync(Adapter(Luid), null, Ct);
        var capabilities = await provider.GetCapabilitiesAsync(Adapter(Luid), Ct);

        Assert.Equal(DataStatus.Failed, telemetry.TemperatureC.Status);
        Assert.Equal(DataStatus.Failed, telemetry.ThrottleReasons.Status);
        Assert.Equal(DataStatus.Failed, settings.Settings.Status);
        Assert.NotEmpty(capabilities.Capabilities);
    }

    [Fact]
    public async Task Adapter_without_luid_or_pci_is_unmatched()
    {
        Api(NvFakes.Gpu(1, Luid));

        Assert.False(await Provider().IsAvailableAsync(Adapter(null), Ct));
    }
}
