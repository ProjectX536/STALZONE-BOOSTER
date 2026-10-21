using System.Text.RegularExpressions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.TestSupport;

public sealed class FakeFileProbe : IFileProbe
{
    private readonly Dictionary<string, string> _contents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _versions = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileProbe Add(string path, string content = "", string? version = null)
    {
        lock (_contents)
        {
            _contents[path] = content;
            if (version is not null)
                _versions[path] = version;
        }
        return this;
    }

    public void Remove(string path)
    {
        lock (_contents)
            _contents.Remove(path);
    }

    public bool FileExists(string path)
    {
        lock (_contents)
            return _contents.ContainsKey(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        var mask = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
        lock (_contents)
        {
            return _contents.Keys
                .Where(f => string.Equals(Path.GetDirectoryName(f), Path.TrimEndingDirectorySeparator(directory),
                    StringComparison.OrdinalIgnoreCase))
                .Where(f => mask.IsMatch(Path.GetFileName(f)))
                .ToArray();
        }
    }

    public string? GetFileVersion(string path)
    {
        lock (_contents)
            return _versions.GetValueOrDefault(path);
    }

    public TextReader OpenText(string path)
    {
        lock (_contents)
        {
            return _contents.TryGetValue(path, out var content)
                ? new StringReader(content)
                : throw new FileNotFoundException("No such file in FakeFileProbe.", path);
        }
    }
}
