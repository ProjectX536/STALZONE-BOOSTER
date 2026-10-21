using Booster.App.Services;

namespace Booster.App.Tests.Services;

/// <summary>Проверяется только построение адреса: тест не запускает процессы и не открывает окна.</summary>
public sealed class LinkOpenerTests
{
    [Theory]
    [InlineData("gaming-gamebar")]
    [InlineData("gaming-gamedvr")]
    [InlineData("gaming-gamemode")]
    [InlineData("display-advancedgraphics")]
    public void Known_settings_page_becomes_ms_settings_uri(string page)
    {
        Assert.Equal("ms-settings:" + page, LinkOpener.TryBuildSettingsUri(page));
    }

    [Theory]
    [InlineData("windowsupdate")]
    [InlineData("gaming-gamemode ")]
    [InlineData("Gaming-GameMode")]
    [InlineData("ms-settings:gaming-gamemode")]
    [InlineData("gaming-gamemode&x=1")]
    [InlineData("")]
    public void Only_known_settings_pages_open(string page)
    {
        Assert.Null(LinkOpener.TryBuildSettingsUri(page));
    }
}
