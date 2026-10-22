using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.GameNetwork;

public class BufferbloatGraderTests
{
    private static readonly double[] Bounds = [5, 30, 60, 200, 400];

    [Theory]
    [InlineData(4.9, BufferbloatGrade.APlus)]
    [InlineData(5, BufferbloatGrade.A)]
    [InlineData(29, BufferbloatGrade.A)]
    [InlineData(30, BufferbloatGrade.B)]
    [InlineData(59, BufferbloatGrade.B)]
    [InlineData(60, BufferbloatGrade.C)]
    [InlineData(199, BufferbloatGrade.C)]
    [InlineData(200, BufferbloatGrade.D)]
    [InlineData(399, BufferbloatGrade.D)]
    [InlineData(400, BufferbloatGrade.F)]
    [InlineData(-3, BufferbloatGrade.APlus)]
    public void Grade_follows_scale(double increaseMs, BufferbloatGrade expected) =>
        Assert.Equal(expected, BufferbloatGrader.Grade(increaseMs, Bounds));

    [Fact]
    public void Empty_scale_has_no_grade() => Assert.Null(BufferbloatGrader.Grade(10, []));
}
