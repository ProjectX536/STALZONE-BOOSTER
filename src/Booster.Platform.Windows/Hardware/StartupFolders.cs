using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Папки «Автозагрузка» пользователя и всех пользователей.</summary>
public interface IStartupFolders
{
    /// <summary>Файлы папки <see cref="StartupLocation.UserFolder"/> или <see cref="StartupLocation.CommonFolder"/>; нет папки — пусто.</summary>
    IReadOnlyList<string> List(StartupLocation location);

    /// <summary>Цель ярлыка; null — ярлык не прочитан или цели-файла нет.</summary>
    string? ResolveShortcut(string path);
}

public sealed class StartupFolders : IStartupFolders
{
    public IReadOnlyList<string> List(StartupLocation location)
    {
        var folder = Environment.GetFolderPath(location switch
        {
            StartupLocation.UserFolder => Environment.SpecialFolder.Startup,
            StartupLocation.CommonFolder => Environment.SpecialFolder.CommonStartup,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
        }, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return [];

        // Только верхний уровень: Проводник не запускает файлы из вложенных папок.
        return [.. Directory.GetFiles(folder).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public string? ResolveShortcut(string path) => ShortcutResolver.Resolve(path);
}
