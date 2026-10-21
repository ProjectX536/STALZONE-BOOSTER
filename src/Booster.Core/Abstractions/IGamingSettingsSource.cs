namespace Booster.Core.Abstractions;

public sealed record GamingSettings(Reading<bool> GameMode, Reading<bool> BackgroundRecording);

public interface IGamingSettingsSource
{
    GamingSettings Read();
}
