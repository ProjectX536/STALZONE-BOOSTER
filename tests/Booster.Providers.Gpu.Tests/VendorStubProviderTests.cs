using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Tests.TestSupport;
using Booster.Providers.Gpu.Amd;
using Booster.Providers.Gpu.Intel;
using Booster.Providers.Gpu.Resources;
using static Booster.Providers.Gpu.Tests.TestSupport.GpuFakes;

namespace Booster.Providers.Gpu.Tests;

public sealed class VendorStubProviderTests
{
    private static readonly string AdlxPath = Path.Combine(Environment.SystemDirectory, AmdProvider.LibraryName);
    private static readonly string IgclPath = Path.Combine(Environment.SystemDirectory, IntelGpuProvider.LibraryName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Amd_reports_vendor_api_not_supported_with_license_note()
    {
        var provider = new AmdProvider(new FakeFileProbe());
        var adapter = Adapter(Luid, GpuVendor.Amd);

        Assert.Equal(("providers.gpu.amd", GpuVendor.Amd), (provider.ProviderId, provider.Vendor));
        Assert.False(await provider.IsAvailableAsync(adapter, Ct));
        var report = Assert.Single((await provider.GetCapabilitiesAsync(adapter, Ct)).Capabilities);
        Assert.Equal((CapabilityIds.GpuVendorApi, CapabilityScopes.Adapter(Luid), CapabilityStatus.NotSupported, provider.ProviderId),
            (report.CapabilityId, report.Scope, report.Status, report.ProviderId));
        Assert.Contains(ProviderStrings.AmdLicenseNote, report.Note, StringComparison.Ordinal);
        Assert.Contains(Format(ProviderStrings.VendorLibraryMissingFormat, "amdadlx64.dll"), report.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intel_reports_vendor_api_not_supported_with_license_note()
    {
        var provider = new IntelGpuProvider(new FakeFileProbe());
        var adapter = Adapter(Luid, GpuVendor.Intel);

        Assert.Equal(("providers.gpu.intel", GpuVendor.Intel), (provider.ProviderId, provider.Vendor));
        Assert.False(await provider.IsAvailableAsync(adapter, Ct));
        var report = Assert.Single((await provider.GetCapabilitiesAsync(adapter, Ct)).Capabilities);
        Assert.Equal((CapabilityIds.GpuVendorApi, CapabilityScopes.Adapter(Luid), CapabilityStatus.NotSupported, provider.ProviderId),
            (report.CapabilityId, report.Scope, report.Status, report.ProviderId));
        Assert.Contains(ProviderStrings.IntelLicenseNote, report.Note, StringComparison.Ordinal);
        Assert.Contains(Format(ProviderStrings.VendorLibraryMissingFormat, "ControlLib.dll"), report.Note, StringComparison.Ordinal);
        Assert.Contains(ProviderStrings.IntelHybridSeeInventory, report.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installed_vendor_library_is_mentioned_but_not_loaded()
    {
        var files = new FakeFileProbe().Add(AdlxPath).Add(IgclPath);
        var amd = new AmdProvider(files);
        var intel = new IntelGpuProvider(files);

        var amdNote = Assert.Single((await amd.GetCapabilitiesAsync(Adapter(Luid, GpuVendor.Amd), Ct)).Capabilities).Note;
        var intelNote = Assert.Single((await intel.GetCapabilitiesAsync(Adapter(Luid, GpuVendor.Intel), Ct)).Capabilities).Note;

        Assert.Contains(Format(ProviderStrings.VendorLibraryInstalledFormat, "amdadlx64.dll"), amdNote, StringComparison.Ordinal);
        Assert.Contains(Format(ProviderStrings.VendorLibraryInstalledFormat, "ControlLib.dll"), intelNote, StringComparison.Ordinal);
        Assert.False(await amd.IsAvailableAsync(Adapter(Luid, GpuVendor.Amd), Ct));
        Assert.False(await intel.IsAvailableAsync(Adapter(Luid, GpuVendor.Intel), Ct));
    }

    [Fact]
    public async Task Stub_telemetry_and_settings_carry_the_license_note()
    {
        var provider = new AmdProvider(new FakeFileProbe());
        var adapter = Adapter(Luid, GpuVendor.Amd);

        var telemetry = await provider.ReadTelemetryAsync(adapter, Ct);
        var settings = await provider.ReadSettingsAsync(adapter, null, Ct);

        Assert.Equal(DataStatus.NotSupported, telemetry.TemperatureC.Status);
        Assert.Contains(ProviderStrings.AmdLicenseNote, telemetry.TemperatureC.Note, StringComparison.Ordinal);
        Assert.Equal(DataStatus.NotSupported, settings.Settings.Status);
        Assert.Contains(ProviderStrings.AmdLicenseNote, settings.Settings.Note, StringComparison.Ordinal);
    }

    private static string Format(string format, string library) => string.Format(CultureInfo.CurrentCulture, format, library);
}
