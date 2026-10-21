using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Drivers;
using Booster.Platform.Windows.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Common;

[Trait("Category", "Host")]
public class HostReadOnlyTests
{
    [Fact]
    public async Task Wmi_client_reads_operating_system()
    {
        var cim = new ManagementCimClient(TimeSpan.FromSeconds(30));

        var rows = await cim.QueryAsync(WmiNamespaces.CimV2, "SELECT Caption FROM Win32_OperatingSystem",
            TestContext.Current.CancellationToken);

        var row = Assert.Single(rows);
        Assert.NotNull(row.GetString("Caption"));
    }

    [Fact]
    public async Task Wmi_client_reads_storage_namespace()
    {
        var cim = new ManagementCimClient(TimeSpan.FromSeconds(30));

        var rows = await cim.QueryAsync(WmiNamespaces.Storage, "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk",
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Registry_reader_reads_current_build()
    {
        var registry = new RegistryReader();

        var build = registry.GetValue(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");

        Assert.NotNull(build);
    }

    [Fact]
    public void Registry_reader_returns_null_and_empty_for_missing_key()
    {
        var registry = new RegistryReader();

        Assert.Null(registry.GetValue(RegistryRoot.CurrentUser, @"Software\Booster.Tests\DoesNotExist", "X"));
        Assert.Empty(registry.GetValues(RegistryRoot.CurrentUser, @"Software\Booster.Tests\DoesNotExist"));
    }

    [Fact]
    public async Task Cpu_detector_finds_a_package_and_topology()
    {
        var detector = new CpuDetector(new ManagementCimClient(TimeSpan.FromSeconds(30)), new CpuTopologySource());

        var cpu = await detector.DetectAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(cpu.Packages);
        Assert.True(cpu.Topology.IsAvailable, cpu.Topology.Note);
        Assert.Equal(cpu.Packages.Sum(p => p.Cores.Value), cpu.Topology.Value!.PerformanceCores + cpu.Topology.Value.EfficiencyCores);
    }

    [Fact]
    public async Task Os_detector_reads_build_and_ubr()
    {
        var detector = new OsDetector(new ManagementCimClient(TimeSpan.FromSeconds(30)), new RegistryReader());

        var os = await detector.DetectAsync(TestContext.Current.CancellationToken);

        Assert.True(os.BuildNumber.IsAvailable);
        Assert.True(os.Ubr.IsAvailable);
    }

    [Fact]
    public async Task Ram_detector_finds_installed_memory()
    {
        var memory = await new RamDetector(new ManagementCimClient(TimeSpan.FromSeconds(30)))
            .DetectAsync(TestContext.Current.CancellationToken);

        Assert.True(memory.TotalPhysicalBytes.IsAvailable);
    }

    [Fact]
    public void Dxgi_finds_the_nvidia_adapter()
    {
        var adapters = new DxgiAdapterSource().Enumerate();

        var nvidia = Assert.Single(adapters, a => a.VendorId == PciVendors.Nvidia);
        Assert.True(nvidia.DedicatedVideoMemory > 4UL * 1024 * 1024 * 1024);
        Assert.True(nvidia.HighPerformanceRank >= 0);
        Assert.NotNull(nvidia.IsUma);
    }

    [Fact]
    public void Kmt_reports_flags_for_dxgi_adapters()
    {
        var dxgi = new DxgiAdapterSource().Enumerate();

        var flags = new KmtAdapterTypeSource().Query();

        Assert.Contains(dxgi.Single(a => a.VendorId == PciVendors.Nvidia).Luid, flags.Keys);
    }

    [Theory]
    [InlineData(@"root\Booster_NoSuchNamespace", "SELECT Caption FROM Win32_OperatingSystem")]
    [InlineData(WmiNamespaces.CimV2, "SELECT Name FROM Booster_NoSuchClass")]
    public async Task Wmi_client_reports_missing_namespace_or_class_as_schema_missing(string wmiNamespace, string wql)
    {
        var cim = new ManagementCimClient(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<CimSchemaMissingException>(
            () => cim.QueryAsync(wmiNamespace, wql, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Storage_detector_finds_the_system_volume()
    {
        var capabilities = new CapabilityRegistry();
        var detector = new StorageDetector(new ManagementCimClient(TimeSpan.FromSeconds(30)), capabilities,
            NullLogger<StorageDetector>.Instance);

        var disks = await detector.DetectAsync(TestContext.Current.CancellationToken);

        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)![..2].ToUpperInvariant();
        var system = Assert.Single(disks, d => d.Volumes.Any(v => v.DriveLetter == systemDrive));
        Assert.True(system.Bus.IsAvailable, system.Bus.Note);
        Assert.Equal(CapabilityStatus.Supported, capabilities.Get(CapabilityIds.StorageWmi).Status);
    }

    [Fact]
    public void Display_config_returns_active_paths_on_dxgi_adapters()
    {
        var paths = new DisplayConfigSource().QueryActivePaths();
        if (paths.Count == 0)
            Assert.Skip("no active display paths in this session (RDP or no monitor)");

        var luids = new DxgiAdapterSource().Enumerate().Select(a => a.Luid).ToHashSet();
        Assert.All(paths, p =>
        {
            Assert.True(p.Width > 0 && p.Height > 0);
            Assert.True(p.RefreshNumerator > 0 && p.RefreshDenominator > 0);
            Assert.Contains(p.AdapterLuid, luids);
        });
        Assert.Single(paths, p => p.IsPrimary);
    }

    [Fact]
    public void Firmware_type_is_known()
    {
        var firmware = new FirmwareTypeSource().Get();

        Assert.True(firmware.IsAvailable, firmware.Note);
    }

    [Fact]
    public async Task Pagefile_source_reads_without_error()
    {
        var source = new PagefileSource(new ManagementCimClient(TimeSpan.FromSeconds(30)));

        var configuration = await source.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(DataStatus.Failed, configuration.AutomaticManaged.Status);
    }

    [Fact]
    public async Task Service_inventory_reads_services_with_rpcss()
    {
        var source = new ServiceInventorySource(new ManagementCimClient(TimeSpan.FromSeconds(30)), new RegistryReader());

        var services = await source.ReadAsync(TestContext.Current.CancellationToken);

        var rpc = Assert.Single(services, s => string.Equals(s.Name, "RpcSs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ServiceRunState.Running, rpc.State);
        Assert.NotEmpty(rpc.DependentServices);
        Assert.Contains("Microsoft", rpc.Publisher.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Privilege_info_reads_current_token()
    {
        var state = new WindowsPrivilegeInfo().GetCurrent();

        Assert.NotNull(state);
    }

    [Fact(Timeout = 120_000)]
    public async Task Driver_inventory_lists_more_than_ten_devices()
    {
        var inventory = new DriverInventory(new ManagementCimClient(TimeSpan.FromSeconds(90)));

        var result = await inventory.ScanAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Devices.Count > 10, $"{result.Devices.Count} devices");
    }
}
