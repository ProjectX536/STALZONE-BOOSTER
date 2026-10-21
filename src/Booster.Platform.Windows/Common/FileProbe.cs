using System.Diagnostics;
using System.Text;

namespace Booster.Platform.Windows.Common;

public interface IFileProbe
{
    bool FileExists(string path);

    IEnumerable<string> EnumerateFiles(string directory, string pattern);

    string? GetFileVersion(string path);

    TextReader OpenText(string path);
}

public sealed class FileProbe : IFileProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern) : [];

    public string? GetFileVersion(string path)
    {
        if (!File.Exists(path))
            return null;
        var version = FileVersionInfo.GetVersionInfo(path).FileVersion?.Trim();
        return string.IsNullOrEmpty(version) ? null : version;
    }

    public TextReader OpenText(string path) => new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
}
