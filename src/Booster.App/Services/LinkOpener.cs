using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Booster.Core.Abstractions;
using Booster.Core.Drivers;
using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

public interface ISettingsOpener
{
    bool TryOpenSettings(string page);

    bool RevealFile(string path);
}

public sealed partial class LinkOpener(OfficialSourceResolver sources, IPrivilegeInfo privileges, ILogger<LinkOpener> logger) : ISettingsOpener
{
    private const string SettingsScheme = "ms-settings:";

    private static readonly string[] SettingsPages = ["gaming-gamebar", "gaming-gamedvr", "gaming-gamemode", "display-advancedgraphics"];
    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!sources.IsAllowed(uri))
        {
            LogRejected(uri.OriginalString);
            return false;
        }

        return Start(ShellStartInfo(uri.AbsoluteUri, privileges.GetCurrent().IsElevated), uri.AbsoluteUri);
    }

    public bool TryOpenSettings(string page)
    {
        if (TryBuildSettingsUri(page) is not { } uri)
        {
            LogSettingsRejected(page);
            return false;
        }

        return Start(ShellStartInfo(uri, privileges.GetCurrent().IsElevated), uri);
    }

    // ShellExecute из повышенного процесса запустил бы браузер с правами администратора,
    // а explorer.exe передаёт адрес уже запущенной оболочке пользователя.
    internal static ProcessStartInfo ShellStartInfo(string target, bool elevated) => elevated
        ? new ProcessStartInfo(ExplorerPath, $"\"{target}\"") { UseShellExecute = false }
        : new ProcessStartInfo(target) { UseShellExecute = true };

    private static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    internal static string? TryBuildSettingsUri(string? page) =>
        page is not null && SettingsPages.Contains(page, StringComparer.Ordinal) ? SettingsScheme + page : null;

    public bool RevealFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        // Кавычек в путях Windows не бывает, экранировать нечего.
        return Start(new ProcessStartInfo(ExplorerPath, $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = false }, path);
    }

    private bool Start(ProcessStartInfo info, string target)
    {
        try
        {
            using var process = Process.Start(info);
            LogOpened(target);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            LogOpenFailed(ex, target);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened {Target}")]
    private partial void LogOpened(string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Target}")]
    private partial void LogOpenFailed(Exception exception, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Link rejected, not an official source: {Uri}")]
    private partial void LogRejected(string uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings page rejected, not in the allow list: {Page}")]
    private partial void LogSettingsRejected(string? page);
}
