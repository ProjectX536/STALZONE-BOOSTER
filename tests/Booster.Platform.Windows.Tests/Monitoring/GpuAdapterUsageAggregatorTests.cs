using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class GpuAdapterUsageAggregatorTests
{
    private static readonly AdapterLuid A = new(0xA, 0);
    private static readonly AdapterLuid B = new(0xB, 0);

    private static GpuEngineSample S(AdapterLuid luid, double percent, string engine = "3D") => new(luid, engine, percent);

    [Fact]
    public void Adapter_with_most_3d_load_over_the_capture_wins()
    {
        var reading = GpuAdapterUsageAggregator.DominantRenderAdapter([[S(A, 10)], [S(A, 20), S(B, 90)]]);

        Assert.Equal(DataStatus.Available, reading.Status);
        Assert.Equal(B, reading.Value);
        Assert.Equal(GpuAdapterUsageAggregator.Source, reading.Source);
    }

    [Fact]
    public void Only_zero_load_is_unknown() =>
        Assert.Equal(DataStatus.Unknown, GpuAdapterUsageAggregator.DominantRenderAdapter([[S(A, 0), S(B, 0)], [S(A, 0)]]).Status);

    [Fact]
    public void Engines_other_than_3d_are_ignored()
    {
        var reading = GpuAdapterUsageAggregator.DominantRenderAdapter(
            [[S(A, 15), S(B, 95, "VideoDecode"), S(B, 80, "Copy"), S(B, 5, "3d")]]);

        Assert.Equal(A, reading.Value);
    }

    [Fact]
    public void No_samples_is_unknown() =>
        Assert.Equal(DataStatus.Unknown, GpuAdapterUsageAggregator.DominantRenderAdapter([]).Status);
}
