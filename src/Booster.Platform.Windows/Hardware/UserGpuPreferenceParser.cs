using System.Globalization;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Значения ключа <c>HKCU\Software\Microsoft\DirectX\UserGpuPreferences</c>: имя — путь к exe,
/// данные — строка вида <c>"SwapEffectUpgradeEnable=1;GpuPreference=2;"</c>.
/// </summary>
public static class UserGpuPreferenceParser
{
    public const string RegistryKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static IReadOnlyList<AppGpuPreference> Parse(IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        var result = new List<AppGpuPreference>();
        foreach (var (name, value) in values)
        {
            if (value is string text && TryReadPreference(text) is { } preference)
                result.Add(new AppGpuPreference(name, preference));
        }

        return result;
    }

    private static AppGpuPreferenceKind? TryReadPreference(string text)
    {
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator < 0 || !part.AsSpan(0, separator).Trim().Equals("GpuPreference", StringComparison.OrdinalIgnoreCase))
                continue;

            return int.TryParse(part.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number switch
                {
                    0 => AppGpuPreferenceKind.SystemDefault,
                    1 => AppGpuPreferenceKind.PowerSaving,
                    2 => AppGpuPreferenceKind.HighPerformance,
                    _ => null,
                }
                : null;
        }

        return null;
    }
}
