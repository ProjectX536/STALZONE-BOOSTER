using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class HagsSourceTests
{
    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Source = $@"HKLM\{GraphicsDrivers}\HwSchMode";
    private const string KeySource = $@"HKLM\{GraphicsDrivers}";

    private readonly FakeRegistryReader _registry = new();

    private Reading<bool> Read() => new HagsSource(_registry).ReadEnabled();

    private Reading<DateTimeOffset> ReadChangedAt() => new HagsSource(_registry).ReadChangedAt();

    [Fact]
    public void Change_time_is_the_key_write_time()
    {
        var writtenAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        _registry.SetWriteTime(RegistryRoot.LocalMachine, GraphicsDrivers, writtenAt);

        Assert.Equal(Reading<DateTimeOffset>.Known(writtenAt, KeySource), ReadChangedAt());
    }

    [Fact]
    public void Missing_key_change_time_is_unknown()
    {
        var reading = ReadChangedAt();

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.Equal(KeySource, reading.Source);
        Assert.False(string.IsNullOrEmpty(reading.Note));
    }

    [Fact]
    public void Unreadable_key_change_time_is_failure()
    {
        _registry.Deny(RegistryRoot.LocalMachine, GraphicsDrivers);

        var reading = ReadChangedAt();

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Equal(KeySource, reading.Source);
    }

    [Fact]
    public void Mode_2_is_enabled()
    {
        _registry.Set(RegistryRoot.LocalMachine, GraphicsDrivers, "HwSchMode", 2);

        Assert.Equal(Reading<bool>.Known(true, Source), Read());
    }

    [Fact]
    public void Mode_1_is_disabled()
    {
        _registry.Set(RegistryRoot.LocalMachine, GraphicsDrivers, "HwSchMode", 1);

        Assert.Equal(Reading<bool>.Known(false, Source), Read());
    }

    /// <summary>Значения нет — Windows решает по GPU и драйверу; другое число или тип — не выдумывается.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData("2")]
    public void Missing_or_unexpected_value_is_unknown(object? value)
    {
        if (value is not null)
            _registry.Set(RegistryRoot.LocalMachine, GraphicsDrivers, "HwSchMode", value);

        var reading = Read();

        Assert.Equal(DataStatus.Unknown, reading.Status);
        Assert.Equal(Source, reading.Source);
        Assert.False(string.IsNullOrEmpty(reading.Note));
    }

    [Fact]
    public void Unreadable_key_is_failure()
    {
        _registry.Deny(RegistryRoot.LocalMachine, GraphicsDrivers);

        var reading = Read();

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Equal(Source, reading.Source);
    }

    /// <summary>Только чтение: ключ HKLM открывается на чтение, HwSchMode не пишется.</summary>
    [Fact]
    [Trait("Category", "Host")]
    public void Reads_real_hags_value()
    {
        var reading = new HagsSource(new RegistryReader()).ReadEnabled();

        Assert.Equal(Source, reading.Source);
    }

    /// <summary>Только чтение: ключ GraphicsDrivers есть в любой Windows 10/11, время его записи — в прошлом и в UTC.</summary>
    [Fact]
    [Trait("Category", "Host")]
    public void Reads_real_key_write_time()
    {
        var reading = new HagsSource(new RegistryReader()).ReadChangedAt();

        Assert.True(reading.IsAvailable, reading.Note);
        Assert.Equal(TimeSpan.Zero, reading.Value.Offset);
        Assert.True(reading.Value <= DateTimeOffset.UtcNow);
    }
}
