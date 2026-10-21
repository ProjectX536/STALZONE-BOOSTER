using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Booster.Core.Abstractions;
using Booster.Core.Drivers;
using Microsoft.Extensions.Logging;

namespace Booster.App.Services;

/// <summary>Открывает страницу «Параметров» Windows, где пользователь меняет настройку сам, и файлы Booster в Проводнике.</summary>
public interface ISettingsOpener
{
    /// <summary>false — страница не из списка разрешённых или не открылась.</summary>
    bool TryOpenSettings(string page);

    /// <summary>Открывает папку файла в Проводнике и выделяет сам файл; false — Проводник не открылся.</summary>
    bool RevealFile(string path);
}

/// <summary>
/// Открывает ссылки и папки во внешних программах. Ссылка открывается, только если её разрешает
/// <see cref="OfficialSourceResolver.IsAllowed"/>: из окна Booster наружу ведут лишь официальные источники.
/// Booster с правами администратора передаёт ссылку Проводнику: иначе браузер унаследовал бы права администратора.
/// </summary>
public sealed partial class LinkOpener(OfficialSourceResolver sources, IPrivilegeInfo privileges, ILogger<LinkOpener> logger) : ISettingsOpener
{
    private const string SettingsScheme = "ms-settings:";

    /// <summary>Страницы «Параметров», на которые ссылаются рекомендации экрана «Система»; другие не открываются.</summary>
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

    /// <summary>
    /// Без прав администратора — ShellExecute. С ними — explorer.exe с адресом: новый процесс Проводника передаёт адрес уже
    /// запущенной оболочке пользователя, и браузер или «Параметры» стартуют без прав администратора. Адрес из списка разрешённых
    /// кавычек не содержит.
    /// </summary>
    internal static ProcessStartInfo ShellStartInfo(string target, bool elevated) => elevated
        ? new ProcessStartInfo(ExplorerPath, $"\"{target}\"") { UseShellExecute = false }
        : new ProcessStartInfo(target) { UseShellExecute = true };

    private static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Адрес ms-settings для страницы из списка; иначе null. Имя сравнивается точно, без обрезки пробелов.</summary>
    internal static string? TryBuildSettingsUri(string? page) =>
        page is not null && SettingsPages.Contains(page, StringComparer.Ordinal) ? SettingsScheme + page : null;

    /// <summary>Открывает папку файла в Проводнике и выделяет сам файл.</summary>
    public bool RevealFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        // Кавычка в пути Windows недопустима, поэтому путь заключается в кавычки без экранирования.
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
