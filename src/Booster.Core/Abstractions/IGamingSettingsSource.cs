namespace Booster.Core.Abstractions;

/// <summary>
/// Игровые настройки Windows текущего пользователя. Только чтение: Booster эти ключи не пишет (GameDVR — никогда).
/// Включён ли Game Bar, не читается: надёжного значения для всех сборок нет.
/// </summary>
/// <param name="GameMode">HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled: 1 → true, 0 → false, нет значения → Unknown (по умолчанию Windows его не пишет).</param>
/// <param name="BackgroundRecording">HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled.</param>
public sealed record GamingSettings(Reading<bool> GameMode, Reading<bool> BackgroundRecording);

public interface IGamingSettingsSource
{
    GamingSettings Read();
}
