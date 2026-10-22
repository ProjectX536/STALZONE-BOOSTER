using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.GameNetwork;

public class WifiBandResolverTests
{
    [Theory]
    [InlineData(6, WifiBand.Band24GHz)]
    [InlineData(11, WifiBand.Band24GHz)]
    [InlineData(14, WifiBand.Band24GHz)]
    [InlineData(36, WifiBand.Band5GHz)]
    [InlineData(100, WifiBand.Band5GHz)]
    [InlineData(144, WifiBand.Band5GHz)]
    [InlineData(197, WifiBand.Band6GHz)]
    [InlineData(233, WifiBand.Band6GHz)]
    public void FromChannel_UnambiguousChannel_IsKnown(int channel, WifiBand expected)
    {
        var reading = WifiBandResolver.FromChannel(channel);

        Assert.Equal(DataStatus.Available, reading.Status);
        Assert.Equal(expected, reading.Value);
        Assert.Equal("Wi-Fi channel number", reading.Source);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(149)]
    [InlineData(165)]
    [InlineData(2)]
    public void FromChannel_AmbiguousChannel_IsUnknown(int channel)
    {
        var reading = WifiBandResolver.FromChannel(channel);

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.Equal("channel is ambiguous between bands", reading.Note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void FromChannel_UnknownChannel_IsUnknown(int channel)
    {
        var reading = WifiBandResolver.FromChannel(channel);

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.Equal("unknown channel", reading.Note);
    }
}
