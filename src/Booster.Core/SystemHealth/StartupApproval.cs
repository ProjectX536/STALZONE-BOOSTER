using System.Buffers.Binary;
using Booster.Core.Abstractions;

namespace Booster.Core.SystemHealth;

public static class StartupApproval
{
    private const byte DisabledFlag = 0x01;
    private const byte TaskManagerDisabled = 0x03;
    private const int Length = 12;

    public static StartupState Parse(byte[]? value) => value switch
    {
        null => StartupState.Enabled,
        [] => StartupState.Unknown,
        [var flags, ..] => (flags & DisabledFlag) != 0 ? StartupState.Disabled : StartupState.Enabled,
    };

    public static byte[] Disabled(DateTimeOffset at)
    {
        var value = new byte[Length];
        value[0] = TaskManagerDisabled;
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(4), at.UtcDateTime.ToFileTimeUtc());
        return value;
    }
}
