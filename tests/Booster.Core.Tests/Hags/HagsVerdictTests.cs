using Booster.Core.Benchmark;
using Booster.Core.Hags;

namespace Booster.Core.Tests.Hags;

public sealed class HagsVerdictTests
{
    [Theory]
    [InlineData(true, ChangeVerdict.Improved, ChangeVerdict.Improved, HagsVerdict.Keep)]
    [InlineData(true, ChangeVerdict.Improved, ChangeVerdict.NoChange, HagsVerdict.Keep)]
    [InlineData(true, ChangeVerdict.NoChange, ChangeVerdict.Improved, HagsVerdict.Keep)]
    [InlineData(true, ChangeVerdict.Worsened, ChangeVerdict.NoChange, HagsVerdict.Revert)]
    [InlineData(true, ChangeVerdict.NoChange, ChangeVerdict.Worsened, HagsVerdict.Revert)]
    [InlineData(true, ChangeVerdict.Worsened, ChangeVerdict.Worsened, HagsVerdict.Revert)]
    [InlineData(true, ChangeVerdict.Improved, ChangeVerdict.Worsened, HagsVerdict.Inconclusive)]
    [InlineData(true, ChangeVerdict.Worsened, ChangeVerdict.Improved, HagsVerdict.Inconclusive)]
    [InlineData(true, ChangeVerdict.Inconclusive, ChangeVerdict.Improved, HagsVerdict.Inconclusive)]
    [InlineData(true, ChangeVerdict.Worsened, ChangeVerdict.Inconclusive, HagsVerdict.Inconclusive)]
    [InlineData(true, ChangeVerdict.NoChange, ChangeVerdict.NoChange, HagsVerdict.NoDifference)]
    [InlineData(false, ChangeVerdict.Improved, ChangeVerdict.Improved, HagsVerdict.Inconclusive)]
    public void Verdict_from_comparison(bool comparable, ChangeVerdict averageFps, ChangeVerdict onePercentLow, HagsVerdict expected)
    {
        var comparison = new BenchmarkComparison("base", "candidate", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "HAGS", comparable,
            [], [], [], averageFps, onePercentLow);

        Assert.Equal(expected, HagsVerdicts.From(comparison));
    }
}
