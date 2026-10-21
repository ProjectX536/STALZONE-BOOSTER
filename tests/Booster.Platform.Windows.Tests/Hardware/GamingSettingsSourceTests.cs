using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class GamingSettingsSourceTests
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    private readonly FakeRegistryReader _registry = new();

    private GamingSettings Read() => new GamingSettingsSource(_registry).Read();

    [Fact]
    public void Missing_values_are_unknown()
    {
        var settings = Read();

        Assert.Equal(DataStatus.Unknown, settings.GameMode.Status);
        Assert.Equal(DataStatus.Unknown, settings.BackgroundRecording.Status);
    }

    [Fact]
    public void Unexpected_number_is_unknown()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, GameBar, "AutoGameModeEnabled", 2)
            .Set(RegistryRoot.CurrentUser, GameDvr, "HistoricalCaptureEnabled", "1");

        var settings = Read();

        Assert.Equal(DataStatus.Unknown, settings.GameMode.Status);
        Assert.Contains("2", settings.GameMode.Note, StringComparison.Ordinal);
        Assert.Equal(DataStatus.Unknown, settings.BackgroundRecording.Status);
    }

    [Fact]
    public void Reads_flags()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, GameBar, "AutoGameModeEnabled", 1)
            .Set(RegistryRoot.CurrentUser, GameDvr, "HistoricalCaptureEnabled", 0);

        var settings = Read();

        Assert.Equal(Reading<bool>.Known(true, $@"HKCU\{GameBar}\AutoGameModeEnabled"), settings.GameMode);
        Assert.Equal(Reading<bool>.Known(false, $@"HKCU\{GameDvr}\HistoricalCaptureEnabled"), settings.BackgroundRecording);
    }

    [Fact]
    public void Unreadable_key_fails_only_its_value()
    {
        _registry
            .Set(RegistryRoot.CurrentUser, GameBar, "AutoGameModeEnabled", 1)
            .Deny(RegistryRoot.CurrentUser, GameDvr);

        var settings = Read();

        Assert.True(settings.GameMode.Value);
        Assert.Equal(DataStatus.Failed, settings.BackgroundRecording.Status);
    }

    /// <summary>Только чтение: ключи HKCU открываются на чтение.</summary>
    [Fact]
    [Trait("Category", "Host")]
    public void Reads_real_gaming_settings()
    {
        var settings = new GamingSettingsSource(new RegistryReader()).Read();

        Assert.NotEqual(DataStatus.Failed, settings.GameMode.Status);
        Assert.NotEqual(DataStatus.Failed, settings.BackgroundRecording.Status);
    }
}
