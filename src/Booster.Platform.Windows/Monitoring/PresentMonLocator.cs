using System.Text.RegularExpressions;
using Booster.Core.Configuration;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Monitoring;

public sealed record PresentMonLocation(string ExecutablePath, string? Version);

public sealed partial class PresentMonLocator(IFileProbe files, IBoosterConfiguration config, IAppPaths paths)
{
    private const string FileMask = "PresentMon-*-x64.exe";

    public PresentMonLocation? Locate()
    {
        var configured = config.Current.Settings.PresentMon.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured) && files.FileExists(configured))
            return new PresentMonLocation(configured, files.GetFileVersion(configured) ?? VersionFromName(configured)?.ToString());

        return Newest(Path.Combine(paths.Tools, "PresentMon")) ?? Newest(paths.BaseDirectory);
    }

    private PresentMonLocation? Newest(string directory)
    {
        var newest = files.EnumerateFiles(directory, FileMask)
            .Select(path => (Path: path, Version: VersionFromName(path)))
            .Where(f => f.Version is not null)
            .OrderByDescending(f => f.Version)
            .FirstOrDefault();

        // У релизов PresentMon 2.x нет ресурса версии, поэтому запасной вариант — версия из имени файла.
        return newest.Path is null
            ? null
            : new PresentMonLocation(newest.Path, files.GetFileVersion(newest.Path) ?? newest.Version!.ToString());
    }

    private static Version? VersionFromName(string path)
    {
        var match = FileName().Match(Path.GetFileName(path));
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version) ? version : null;
    }

    [GeneratedRegex(@"^PresentMon-(?<version>\d+\.\d+\.\d+)-x64\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileName();
}
