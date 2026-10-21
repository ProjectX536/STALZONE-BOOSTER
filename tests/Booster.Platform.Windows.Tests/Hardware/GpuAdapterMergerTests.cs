using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class GpuAdapterMergerTests
{
    private static readonly AdapterLuid NvidiaLuid = new(0x0000D2E3, 0);
    private static readonly AdapterLuid IntelLuid = new(0x0000C1A0, 0);
    private static readonly AdapterLuid BasicRenderLuid = new(0x0000E1A2, 0);

    private static readonly DxgiAdapterRecord DesktopNvidia = new("NVIDIA GeForce RTX 3060 Ti", 0x10DE, 0x2489, 0xC9721462,
        0xA1, NvidiaLuid, 8_589_934_592, 8_506_955_776, IsSoftware: false, HighPerformanceRank: 0, IsUma: false);

    private static readonly DxgiAdapterRecord BasicRender = new("Microsoft Basic Render Driver", 0x1414, 0x8C, 0, 0,
        BasicRenderLuid, 0, 8_506_955_776, IsSoftware: true, HighPerformanceRank: 1, IsUma: true);

    private static Reading<IReadOnlyList<CimRow>> Wmi(string folder) =>
        Reading<IReadOnlyList<CimRow>>.Known(WmiFixture.Load($"{folder}/Win32_VideoController.json"), "WMI");

    private static Reading<IReadOnlyList<DxgiAdapterRecord>> Dxgi(params DxgiAdapterRecord[] adapters) =>
        Reading<IReadOnlyList<DxgiAdapterRecord>>.Known(adapters, "DXGI");

    private static Reading<IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags>> Kmt(
        params (AdapterLuid Luid, KmtAdapterFlags Flags)[] flags) =>
        Reading<IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags>>.Known(flags.ToDictionary(f => f.Luid, f => f.Flags), "KMT");

    private static readonly Reading<IReadOnlyList<AppGpuPreference>> NoPreferences =
        Reading<IReadOnlyList<AppGpuPreference>>.Known([], "registry");

    [Fact]
    public void Desktop_has_one_discrete_nvidia_with_vram_from_dxgi()
    {
        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(DesktopNvidia, BasicRender), Kmt(), NoPreferences);

        var hardware = Assert.Single(gpu.Adapters, a => !a.IsSoftware);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", hardware.Name);
        Assert.Equal(GpuVendor.Nvidia, hardware.Vendor);
        Assert.Equal(GpuAdapterKind.Discrete, hardware.Kind.Value);
        Assert.Equal(8_589_934_592L, hardware.DedicatedVideoMemoryBytes.Value);
        Assert.Equal(8_506_955_776L, hardware.SharedSystemMemoryBytes.Value);
        Assert.Equal("32.0.15.6094", hardware.DriverVersion.Value);
        Assert.Equal(new DateOnly(2024, 8, 14), hardware.DriverDate.Value);
        Assert.Equal(new PciId(0x10DE, 0x2489, 0xC9721462, 0xA1), hardware.Pci.Value);
        Assert.Equal(NvidiaLuid, hardware.Luid.Value);
        Assert.Equal(0, hardware.HighPerformanceRank.Value);
        Assert.Null(hardware.MatchNote);
        Assert.False(gpu.IsHybrid.Value);
    }

    [Fact]
    public void Basic_render_stays_listed_as_software()
    {
        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(DesktopNvidia, BasicRender), Kmt(), NoPreferences);

        var software = Assert.Single(gpu.Adapters, a => a.IsSoftware);
        Assert.Equal("Microsoft Basic Render Driver", software.Name);
        Assert.Equal(GpuVendor.Microsoft, software.Vendor);
        Assert.Equal(DataStatus.Unknown, software.Kind.Status);
        Assert.Equal(2, gpu.Adapters.Count);
    }

    [Fact]
    public void Hybrid_laptop_uses_kmt_flags()
    {
        var nvidia = new DxgiAdapterRecord("NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 0x28E0, 0x00021234, 0xA1,
            NvidiaLuid, 8_585_740_288, 16_000_000_000, false, 0, IsUma: null);
        var intel = new DxgiAdapterRecord("Intel(R) UHD Graphics", 0x8086, 0xA7A8, 0x00011234, 0x04,
            IntelLuid, 134_217_728, 16_000_000_000, false, 1, IsUma: null);

        var gpu = GpuAdapterMerger.Merge(Wmi("laptop-hybrid.synthetic"), Dxgi(nvidia, intel),
            Kmt((NvidiaLuid, new(HybridDiscrete: true, HybridIntegrated: false, SoftwareDevice: false)),
                (IntelLuid, new(HybridDiscrete: false, HybridIntegrated: true, SoftwareDevice: false))),
            NoPreferences);

        Assert.True(gpu.IsHybrid.Value);
        var nv = Assert.Single(gpu.Adapters, a => a.Vendor == GpuVendor.Nvidia);
        var igpu = Assert.Single(gpu.Adapters, a => a.Vendor == GpuVendor.Intel);
        Assert.Equal(GpuAdapterKind.Discrete, nv.Kind.Value);
        Assert.Equal(GpuAdapterKind.Integrated, igpu.Kind.Value);
        Assert.Equal(0, nv.HighPerformanceRank.Value);
        Assert.Equal(1, igpu.HighPerformanceRank.Value);
        Assert.Equal("31.0.101.5333", igpu.DriverVersion.Value);
    }

    [Fact]
    public void Uma_and_non_uma_hardware_adapters_make_hybrid_without_kmt_flags()
    {
        var nvidia = DesktopNvidia with { IsUma = false };
        var intel = new DxgiAdapterRecord("Intel(R) UHD Graphics 730", 0x8086, 0x4692, 0x7D481462, 0x0C, IntelLuid,
            134_217_728, 8_000_000_000, false, 1, IsUma: true);

        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(nvidia, intel), Kmt(), NoPreferences);

        Assert.True(gpu.IsHybrid.Value);
        Assert.Equal(GpuAdapterKind.Integrated, Assert.Single(gpu.Adapters, a => a.Vendor == GpuVendor.Intel).Kind.Value);
    }

    [Fact]
    public void Two_adapters_without_flags_or_uma_are_unknown()
    {
        var first = DesktopNvidia with { IsUma = null };
        var second = new DxgiAdapterRecord("AMD Radeon RX 7600", 0x1002, 0x7480, 0x00011234, 0xCF, IntelLuid,
            8_573_157_376, 8_000_000_000, false, 1, IsUma: null);

        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(first, second), Kmt(), NoPreferences);

        Assert.All(gpu.Adapters, a => Assert.Equal(DataStatus.Unknown, a.Kind.Status));
        Assert.Equal(DataStatus.Unknown, gpu.IsHybrid.Status);
    }

    [Fact]
    public void Failed_dxgi_leaves_wmi_adapters_without_vram_and_luid()
    {
        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"),
            Reading<IReadOnlyList<DxgiAdapterRecord>>.Failure("DXGI", "COMException: no factory"), Kmt(), NoPreferences);

        var adapter = Assert.Single(gpu.Adapters);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", adapter.Name);
        Assert.Equal(GpuVendor.Nvidia, adapter.Vendor);
        Assert.Equal(DataStatus.Unknown, adapter.DedicatedVideoMemoryBytes.Status);
        Assert.Contains("COMException", adapter.DedicatedVideoMemoryBytes.Note);
        Assert.Equal(DataStatus.Unknown, adapter.Luid.Status);
        Assert.NotNull(adapter.Luid.Note);
        Assert.Equal("32.0.15.6094", adapter.DriverVersion.Value);
        Assert.Equal(new PciId(0x10DE, 0x2489, 0xC9721462, 0xA1), adapter.Pci.Value);
    }

    [Fact]
    public void Failed_wmi_leaves_dxgi_adapters_without_driver_data()
    {
        var gpu = GpuAdapterMerger.Merge(Reading<IReadOnlyList<CimRow>>.Failure("WMI", "ManagementException: denied"),
            Dxgi(DesktopNvidia, BasicRender), Kmt(), NoPreferences);

        Assert.Equal(["NVIDIA GeForce RTX 3060 Ti", "Microsoft Basic Render Driver"], gpu.Adapters.Select(a => a.Name));
        var nvidia = gpu.Adapters[0];
        Assert.Equal(DataStatus.Unknown, nvidia.DriverVersion.Status);
        Assert.Contains("ManagementException", nvidia.DriverVersion.Note);
        Assert.Equal(DataStatus.Unknown, nvidia.DriverDate.Status);
        Assert.Equal(8_589_934_592L, nvidia.DedicatedVideoMemoryBytes.Value);
    }

    [Fact]
    public void Identical_cards_are_matched_in_order_with_a_note()
    {
        var wmiRow = WmiFixture.LoadSingle("desktop-nvidia/Win32_VideoController.json");
        var secondLuid = new AdapterLuid(0x0000D300, 0);

        var gpu = GpuAdapterMerger.Merge(Reading<IReadOnlyList<CimRow>>.Known([wmiRow, wmiRow], "WMI"),
            Dxgi(DesktopNvidia, DesktopNvidia with { Luid = secondLuid, HighPerformanceRank = 1 }), Kmt(), NoPreferences);

        Assert.Equal(2, gpu.Adapters.Count);
        Assert.All(gpu.Adapters, a =>
        {
            Assert.Equal("32.0.15.6094", a.DriverVersion.Value);
            Assert.Equal("сопоставлено по порядку", a.MatchNote);
        });
        Assert.Equal([NvidiaLuid, secondLuid], gpu.Adapters.Select(a => a.Luid.Value));
    }

    [Fact]
    public void Wmi_adapter_without_pci_id_is_listed_as_software()
    {
        var remote = WmiFixture.Row(("Name", "Microsoft Remote Display Adapter"),
            ("PNPDeviceID", @"SWD\REMOTEDISPLAYENUM\RDPIDD"), ("DriverVersion", "10.0.26100.1"));
        var rows = WmiFixture.Load("desktop-nvidia/Win32_VideoController.json").Append(remote).ToList();

        var gpu = GpuAdapterMerger.Merge(Reading<IReadOnlyList<CimRow>>.Known(rows, "WMI"), Dxgi(DesktopNvidia), Kmt(),
            NoPreferences);

        var adapter = Assert.Single(gpu.Adapters, a => a.Name == "Microsoft Remote Display Adapter");
        Assert.True(adapter.IsSoftware);
        Assert.Equal(DataStatus.Unknown, adapter.Pci.Status);
        Assert.False(gpu.IsHybrid.Value);
    }

    [Fact]
    public void Software_flag_from_kmt_is_not_hybrid()
    {
        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(DesktopNvidia, BasicRender),
            Kmt((BasicRenderLuid, new(HybridDiscrete: false, HybridIntegrated: true, SoftwareDevice: true))), NoPreferences);

        Assert.False(gpu.IsHybrid.Value);
    }

    [Fact]
    public void Negative_rank_is_unknown()
    {
        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(DesktopNvidia with { HighPerformanceRank = -1 }), Kmt(),
            NoPreferences);

        Assert.Equal(DataStatus.Unknown, Assert.Single(gpu.Adapters).HighPerformanceRank.Status);
    }

    [Fact]
    public void Preferences_are_passed_through()
    {
        var preferences = Reading<IReadOnlyList<AppGpuPreference>>.Known(
            [new(@"C:\Games\Game.exe", AppGpuPreferenceKind.HighPerformance)], "registry");

        var gpu = GpuAdapterMerger.Merge(Wmi("desktop-nvidia"), Dxgi(DesktopNvidia), Kmt(), preferences);

        Assert.Same(preferences.Value, gpu.AppPreferences.Value);
    }
}
