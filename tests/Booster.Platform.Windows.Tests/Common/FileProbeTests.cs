using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class FileProbeTests
{
    private readonly FileProbe _probe = new();
    private static readonly string Missing = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_directory_enumerates_as_empty()
    {
        Assert.Empty(_probe.EnumerateFiles(Missing, "*.exe"));
    }

    [Fact]
    public void Missing_file_has_no_version()
    {
        Assert.False(_probe.FileExists(Path.Combine(Missing, "PresentMon.exe")));
        Assert.Null(_probe.GetFileVersion(Path.Combine(Missing, "PresentMon.exe")));
    }

    [Fact]
    public void Version_of_existing_assembly_is_read()
    {
        var assembly = typeof(FileProbe).Assembly.Location;

        Assert.True(_probe.FileExists(assembly));
        Assert.NotNull(_probe.GetFileVersion(assembly));
        Assert.Contains(assembly, _probe.EnumerateFiles(Path.GetDirectoryName(assembly)!, "Booster.Platform.Windows.dll"),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Text_is_opened_as_utf8_without_bom()
    {
        var csv = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PresentMon", "pm-header-only.csv");

        using var reader = _probe.OpenText(csv);

        Assert.StartsWith("Application,", reader.ReadLine(), StringComparison.Ordinal);
    }
}
