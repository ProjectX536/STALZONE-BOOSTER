using Booster.Core.Configuration;

namespace Booster.Platform.Windows.Common;

/// <summary>Встроенные файлы — <see cref="IAppPaths.BuiltInConfig"/>, пользовательские — <see cref="IAppPaths.UserConfig"/>.</summary>
public sealed class ConfigFileSource(IAppPaths paths) : IConfigFileSource
{
    public string ReadBuiltIn(string fileName) => File.ReadAllText(Path.Combine(paths.BuiltInConfig, CheckName(fileName)));

    public string? ReadUser(string fileName)
    {
        var path = Path.Combine(paths.UserConfig, CheckName(fileName));
        AtomicFile.DeleteTemp(path);
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task WriteUserAsync(string fileName, string json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(json);
        return AtomicFile.WriteAllTextAsync(Path.Combine(paths.UserConfig, CheckName(fileName)), json, ct);
    }

    private static string CheckName(string fileName) => FileNames.CheckBare(fileName);
}
