using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class GpuSystemCounterSourceTests
{
    private static readonly AdapterLuid A = new(0x0000C2A1, 0);
    private static readonly AdapterLuid B = new(0x0000A001, 0);

    private static string Engine(int pid, AdapterLuid luid) => $"pid_{pid}_luid_{luid}_phys_0_eng_0_engtype_3D";

    [Fact]
    public void First_sample_has_unknown_utilization_and_known_memory()
    {
        var result = GpuSystemCounterSource.Combine(hasBaseline: false, measured: [], engineInstances: [Engine(100, A)],
            dedicated: new Dictionary<AdapterLuid, long> { [A] = 4096 });

        Assert.Equal(DataStatus.Unknown, result[A].UtilizationPercent.Status);
        Assert.Equal(DataStatus.Available, result[A].DedicatedUsageBytes.Status);
        Assert.Equal(4096, result[A].DedicatedUsageBytes.Value);
    }

    [Fact]
    public void Measured_engines_give_known_utilization()
    {
        var result = GpuSystemCounterSource.Combine(true, [(Engine(100, A), 42)], [Engine(100, A)],
            new Dictionary<AdapterLuid, long> { [A] = 4096 });

        Assert.Equal(DataStatus.Available, result[A].UtilizationPercent.Status);
        Assert.Equal(42, result[A].UtilizationPercent.Value);
    }

    [Fact]
    public void Adapter_without_engine_instances_is_idle()
    {
        var result = GpuSystemCounterSource.Combine(true, [(Engine(100, A), 42)], [Engine(100, A)],
            new Dictionary<AdapterLuid, long> { [A] = 4096, [B] = 0 });

        Assert.Equal(DataStatus.Available, result[B].UtilizationPercent.Status);
        Assert.Equal(0, result[B].UtilizationPercent.Value);
    }

    [Fact]
    public void Adapter_whose_engines_all_appeared_since_the_last_sample_is_unknown()
    {
        var result = GpuSystemCounterSource.Combine(true, [(Engine(100, A), 42)], [Engine(100, A), Engine(300, B)],
            new Dictionary<AdapterLuid, long> { [A] = 4096, [B] = 0 });

        Assert.Equal(DataStatus.Unknown, result[B].UtilizationPercent.Status);
    }

    [Fact]
    public void Adapter_missing_from_memory_counters_has_unknown_memory()
    {
        var result = GpuSystemCounterSource.Combine(true, [(Engine(100, A), 42)], [Engine(100, A)],
            new Dictionary<AdapterLuid, long>());

        Assert.Equal(DataStatus.Unknown, result[A].DedicatedUsageBytes.Status);
        Assert.Equal(42, result[A].UtilizationPercent.Value);
    }
}
