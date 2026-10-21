using Booster.Core.Abstractions;

namespace Booster.Core.Tests.Abstractions;

public class ReadingTests
{
    [Fact]
    public void Known_carries_value_and_source()
    {
        var reading = Reading<int>.Known(5, "WMI");

        Assert.True(reading.IsAvailable);
        Assert.Equal(DataStatus.Available, reading.Status);
        Assert.Equal(5, reading.Value);
        Assert.Equal("WMI", reading.Source);
    }

    [Fact]
    public void Known_rejects_null_value()
    {
        Assert.Throws<ArgumentNullException>(() => Reading<string>.Known(null!, "x"));
    }

    [Fact]
    public void Unknown_has_no_value()
    {
        var reading = Reading<string>.Unknown();

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.Null(reading.Value);
        Assert.False(reading.IsAvailable);
    }

    [Fact]
    public void Failure_keeps_source_and_note()
    {
        var reading = Reading<int>.Failure("WMI", "timeout");

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Equal("WMI", reading.Source);
        Assert.Equal("timeout", reading.Note);
        Assert.False(reading.IsAvailable);
    }

    [Fact]
    public void Unknown_value_type_holds_default_but_is_not_available()
    {
        var reading = Reading<int>.Unknown("WMI");

        Assert.Equal(0, reading.Value);
        Assert.False(reading.IsAvailable);
    }

    [Fact]
    public void NeedsAdmin_and_Unsupported_set_their_statuses()
    {
        Assert.Equal(DataStatus.RequiresAdmin, Reading<long>.NeedsAdmin("SMART", "нужен администратор").Status);
        Assert.Equal(DataStatus.NotSupported, Reading<long>.Unsupported("NVAPI").Status);
    }

    [Fact]
    public void Default_reading_is_unknown_not_available()
    {
        // default(Reading<T>) не должен выглядеть как достоверное значение
        Reading<long> reading = default;

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.False(reading.IsAvailable);
    }
}
