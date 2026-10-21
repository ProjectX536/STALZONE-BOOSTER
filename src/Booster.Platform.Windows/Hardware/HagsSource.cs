using System.Globalization;
using System.Security;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// HAGS из HKLM. Только чтение: ключ GraphicsDrivers открывается на чтение, HwSchMode не пишется.
/// Значения DWORD: 2 → true, 1 → false, другое — Unknown; нет значения — Unknown (Windows решает по GPU и драйверу).
/// Время изменения — время записи ключа (RegQueryInfoKey): у отдельного значения своего времени нет.
/// </summary>
public sealed class HagsSource(IRegistryReader registry) : IHagsSource
{
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string ModeValue = "HwSchMode";
    private const string Source = $@"HKLM\{GraphicsDriversKey}\{ModeValue}";
    private const string KeySource = $@"HKLM\{GraphicsDriversKey}";

    public Reading<bool> ReadEnabled()
    {
        object? value;
        try
        {
            value = registry.GetValue(RegistryRoot.LocalMachine, GraphicsDriversKey, ModeValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Reading<bool>.Failure(Source, $"{ex.GetType().Name}: {ex.Message}");
        }

        return value switch
        {
            null => Reading<bool>.Unknown(Source, "value not set"),
            1 => Reading<bool>.Known(false, Source),
            2 => Reading<bool>.Known(true, Source),
            int number => Reading<bool>.Unknown(Source, string.Create(CultureInfo.InvariantCulture, $"unexpected value {number}")),
            _ => Reading<bool>.Unknown(Source, $"unexpected type {value.GetType().Name}"),
        };
    }

    public Reading<DateTimeOffset> ReadChangedAt()
    {
        DateTimeOffset? writtenAt;
        try
        {
            writtenAt = registry.GetLastWriteTime(RegistryRoot.LocalMachine, GraphicsDriversKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Reading<DateTimeOffset>.Failure(KeySource, $"{ex.GetType().Name}: {ex.Message}");
        }

        return writtenAt is { } value ? Reading<DateTimeOffset>.Known(value, KeySource) : Reading<DateTimeOffset>.Unknown(KeySource, "key not found");
    }
}
