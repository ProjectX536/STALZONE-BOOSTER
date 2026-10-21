using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class CpuMapperTests
{
    [Fact]
    public void Real_processor_maps_to_one_package()
    {
        var package = Assert.Single(CpuMapper.MapPackages(WmiFixture.Load("desktop-nvidia/Win32_Processor.json")));

        Assert.Equal("Intel(R) Core(TM) i5-14400F", package.Name);
        Assert.Equal("GenuineIntel", package.Manufacturer.Value);
        Assert.Equal(10, package.Cores.Value);
        Assert.Equal(16, package.LogicalProcessors.Value);
        Assert.Equal(2500, package.BaseClockMhz.Value);
        Assert.Equal("WMI Win32_Processor.MaxClockSpeed", package.BaseClockMhz.Source);
    }

    [Fact]
    public void Name_is_trimmed()
    {
        var row = WmiFixture.Row(("Name", "  AMD Ryzen 7 7800X3D 8-Core Processor   "), ("NumberOfCores", 8L));

        Assert.Equal("AMD Ryzen 7 7800X3D 8-Core Processor", Assert.Single(CpuMapper.MapPackages([row])).Name);
    }

    [Fact]
    public void Missing_core_count_is_unknown_and_rest_is_filled()
    {
        var row = WmiFixture.Row(("Name", "CPU"), ("Manufacturer", "AuthenticAMD"), ("NumberOfLogicalProcessors", 16L),
            ("MaxClockSpeed", 4200L));

        var package = Assert.Single(CpuMapper.MapPackages([row]));

        Assert.Equal(DataStatus.Unknown, package.Cores.Status);
        Assert.Equal(16, package.LogicalProcessors.Value);
        Assert.Equal(4200, package.BaseClockMhz.Value);
        Assert.Equal("AuthenticAMD", package.Manufacturer.Value);
    }

    [Fact]
    public void Zero_values_are_unknown_not_zero()
    {
        var row = WmiFixture.Row(("Name", "CPU"), ("NumberOfCores", 0L), ("MaxClockSpeed", 0L));

        var package = Assert.Single(CpuMapper.MapPackages([row]));

        Assert.Equal(DataStatus.Unknown, package.Cores.Status);
        Assert.Equal(DataStatus.Unknown, package.BaseClockMhz.Status);
        Assert.Equal(DataStatus.Unknown, package.Manufacturer.Status);
    }

    [Fact]
    public void Every_socket_is_a_package()
    {
        var rows = new[] { WmiFixture.Row(("Name", "Xeon"), ("NumberOfCores", 16L)), WmiFixture.Row(("Name", "Xeon"), ("NumberOfCores", 16L)) };

        Assert.Equal(2, CpuMapper.MapPackages(rows).Count);
    }

    [Fact]
    public void Hybrid_topology_counts_highest_class_as_performance_cores()
    {
        var topology = CpuMapper.MapTopology([1, 1, 1, 1, 1, 1, 0, 0, 0, 0], "test");

        Assert.Equal(DataStatus.Available, topology.Status);
        Assert.Equal(new(6, 4, true), topology.Value);
        Assert.Equal("test", topology.Source);
    }

    [Fact]
    public void Single_efficiency_class_is_not_hybrid()
    {
        Assert.Equal(new(4, 0, false), CpuMapper.MapTopology([0, 0, 0, 0], "test").Value);
    }

    [Fact]
    public void Three_classes_count_only_the_top_one_as_performance()
    {
        Assert.Equal(new(2, 6, true), CpuMapper.MapTopology([2, 2, 1, 1, 1, 1, 0, 0], "test").Value);
    }

    [Fact]
    public void Empty_topology_is_unknown()
    {
        var topology = CpuMapper.MapTopology([], "test");

        Assert.Equal(DataStatus.Unknown, topology.Status);
        Assert.Equal("test", topology.Source);
    }
}
