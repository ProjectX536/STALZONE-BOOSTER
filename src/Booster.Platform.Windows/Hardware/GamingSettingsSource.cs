using System.Globalization;
using System.Security;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public sealed class GamingSettingsSource(IRegistryReader registry) : IGamingSettingsSource
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameModeValue = "AutoGameModeEnabled";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string BackgroundRecordingValue = "HistoricalCaptureEnabled";

    public GamingSettings Read() => new(Flag(GameBarKey, GameModeValue), Flag(GameDvrKey, BackgroundRecordingValue));

    private Reading<bool> Flag(string subKey, string valueName)
    {
        var source = $@"HKCU\{subKey}\{valueName}";
        object? value;
        try
        {
            value = registry.GetValue(RegistryRoot.CurrentUser, subKey, valueName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Reading<bool>.Failure(source, $"{ex.GetType().Name}: {ex.Message}");
        }

        return value switch
        {
            null => Reading<bool>.Unknown(source, "value not set"),
            0 => Reading<bool>.Known(false, source),
            1 => Reading<bool>.Known(true, source),
            int number => Reading<bool>.Unknown(source, string.Create(CultureInfo.InvariantCulture, $"unexpected value {number}")),
            _ => Reading<bool>.Unknown(source, $"unexpected type {value.GetType().Name}"),
        };
    }
}
