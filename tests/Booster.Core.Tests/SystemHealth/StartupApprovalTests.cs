using System.Buffers.Binary;
using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.SystemHealth;

public class StartupApprovalTests
{
    private static byte[] Value(byte first) => [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Absent_value_is_enabled()
    {
        Assert.Equal(StartupState.Enabled, StartupApproval.Parse(null));
    }

    [Theory]
    [InlineData(0x03, StartupState.Disabled)]
    [InlineData(0x02, StartupState.Enabled)]
    [InlineData(0x06, StartupState.Enabled)]
    [InlineData(0x07, StartupState.Disabled)]
    public void Odd_first_byte_is_disabled(byte first, StartupState expected)
    {
        Assert.Equal(expected, StartupApproval.Parse(Value(first)));
    }

    [Fact]
    public void Empty_value_is_unknown()
    {
        Assert.Equal(StartupState.Unknown, StartupApproval.Parse([]));
    }

    [Fact]
    public void Disabled_value_is_12_bytes_with_filetime()
    {
        var at = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.FromHours(3));

        var value = StartupApproval.Disabled(at);

        Assert.Equal(12, value.Length);
        Assert.Equal([3, 0, 0, 0], value[..4]);
        Assert.Equal(at.UtcDateTime.ToFileTimeUtc(), BinaryPrimitives.ReadInt64LittleEndian(value.AsSpan(4)));
        Assert.Equal(StartupState.Disabled, StartupApproval.Parse(value));
    }
}
