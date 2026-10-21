using Booster.Core.Inventory;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class GpuCounterAggregatorTests
{
    private static readonly AdapterLuid A = new(0x0000C2A1, 0);
    private static readonly AdapterLuid B = new(0x0000A001, 0);

    private static string Engine(int pid, AdapterLuid luid, string type, int engine = 0) =>
        $"pid_{pid}_luid_{luid}_phys_0_eng_{engine}_engtype_{type}";

    private static string Memory(AdapterLuid luid, int phys = 0) => $"luid_{luid}_phys_{phys}";

    [Fact]
    public void Utilization_is_max_over_engine_types_of_the_sum_over_processes()
    {
        var result = GpuCounterAggregator.Utilization(
            [(Engine(100, A, "3D"), 30), (Engine(200, A, "3D"), 25), (Engine(100, A, "Copy", 1), 10)]);

        Assert.Equal(55, result[A], precision: 6);
    }

    [Fact]
    public void Utilization_is_capped_at_100()
    {
        var result = GpuCounterAggregator.Utilization([(Engine(100, A, "3D"), 70), (Engine(200, A, "3D"), 60)]);

        Assert.Equal(100, result[A]);
    }

    [Fact]
    public void Instances_of_two_adapters_are_kept_apart()
    {
        var result = GpuCounterAggregator.Utilization([(Engine(100, A, "3D"), 40), (Engine(100, B, "3D"), 15)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(40, result[A]);
        Assert.Equal(15, result[B]);
    }

    [Fact]
    public void Unparsable_instance_is_ignored()
    {
        var result = GpuCounterAggregator.Utilization([("_Total", 50), (Engine(100, A, "3D"), 20)]);

        Assert.Equal(20, Assert.Single(result).Value);
    }

    [Fact]
    public void Dedicated_usage_by_luid()
    {
        var result = GpuCounterAggregator.DedicatedUsage(
            [(Memory(A), 2_230_964_224L), (Memory(B), 0L), ("pid_100_luid_bogus", 5L)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(2_230_964_224L, result[A]);
        Assert.Equal(0L, result[B]);
    }

    [Fact]
    public void Dedicated_usage_of_linked_physical_adapters_is_summed()
    {
        var result = GpuCounterAggregator.DedicatedUsage([(Memory(A, 0), 1000L), (Memory(A, 1), 500L)]);

        Assert.Equal(1500L, result[A]);
    }
}
