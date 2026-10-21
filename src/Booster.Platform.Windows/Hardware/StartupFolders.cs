using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Hardware;

public interface IStartupFolders
{
    IReadOnlyList<string> List(StartupLocation location);

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
