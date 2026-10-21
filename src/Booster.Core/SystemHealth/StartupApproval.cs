using System.Buffers.Binary;
using Booster.Core.Abstractions;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Значения ключей StartupApproved: первый байт — флаги (нечётный — элемент отключён), за ним FILETIME отключения.
/// Формат не документирован Microsoft; так его пишет и читает Диспетчер задач Windows 10/11.
/// </summary>
public static class StartupApproval
{
    private const byte DisabledFlag = 0x01;
    private const byte TaskManagerDisabled = 0x03;
    private const int Length = 12;

    /// <summary>Нет значения → Enabled; первый байт нечётный → Disabled; иначе Enabled; пустой массив → Unknown.</summary>
    public static StartupState Parse(byte[]? value) => value switch
    {
        null => StartupState.Enabled,
        [] => StartupState.Unknown,
        [var flags, ..] => (flags & DisabledFlag) != 0 ? StartupState.Disabled : StartupState.Enabled,
    };

    /// <summary>Как пишет диспетчер задач: 03 00 00 00 и FILETIME (UTC) — 12 байт.</summary>
    public static byte[] Disabled(DateTimeOffset at)
    {
        var value = new byte[Length];
        value[0] = TaskManagerDisabled;
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(4), at.UtcDateTime.ToFileTimeUtc());
        return value;
    }
}
