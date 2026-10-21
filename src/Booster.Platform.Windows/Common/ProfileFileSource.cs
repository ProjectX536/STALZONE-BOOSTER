using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

/// <summary>Встроенные профили — <see cref="IAppPaths.BuiltInProfiles"/>, пользовательские — <see cref="IAppPaths.UserProfiles"/>.</summary>
public sealed class ProfileFileSource(IAppPaths paths) : IProfileFileSource
{
    private const string Extension = ".json";

    public IReadOnlyList<string> ListBuiltIn()
    {
        if (!Directory.Exists(paths.BuiltInProfiles))
            return [];
        return Directory.EnumerateFiles(paths.BuiltInProfiles, "*" + Extension)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string ReadBuiltIn(string fileName) => File.ReadAllText(Path.Combine(paths.BuiltInProfiles, FileNames.CheckBare(fileName)));

    public string? ReadUser(string fileName)
    {
        var path = Path.Combine(paths.UserProfiles, FileNames.CheckBare(fileName));
        AtomicFile.DeleteTemp(path);
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task WriteUserAsync(string fileName, string json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(json);
        var path = Path.Combine(paths.UserProfiles, FileNames.CheckBare(fileName));
        Directory.CreateDirectory(paths.UserProfiles);
        return AtomicFile.WriteAllTextAsync(path, json, ct);
    }
}
