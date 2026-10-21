using System.IO;
using Booster.App.Services;

namespace Booster.App.Tests.Services;

/// <summary>Проверяется только построение адреса и параметров запуска: тест не запускает процессы и не открывает окна.</summary>
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

    [Theory]
    [InlineData("https://www.nvidia.com/Download/index.aspx")]
    [InlineData("ms-settings:gaming-gamemode")]
    public void Not_elevated_booster_opens_target_through_the_shell(string target)
    {
        var info = LinkOpener.ShellStartInfo(target, elevated: false);

        Assert.Equal((target, "", true), (info.FileName, info.Arguments, info.UseShellExecute));
    }

    [Theory]
    [InlineData("https://www.nvidia.com/Download/index.aspx")]
    [InlineData("ms-settings:gaming-gamemode")]
    public void Elevated_booster_hands_target_to_explorer_so_browser_runs_without_admin_rights(string target)
    {
        var info = LinkOpener.ShellStartInfo(target, elevated: true);

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.Equal((explorer, $"\"{target}\"", false), (info.FileName, info.Arguments, info.UseShellExecute));
    }
}
