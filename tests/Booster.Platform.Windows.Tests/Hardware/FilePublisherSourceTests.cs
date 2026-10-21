using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public class FilePublisherSourceTests
{
    [Fact]
    public void Missing_path_or_file_is_unknown()
    {
        var source = new FilePublisherSource();

        Assert.Equal(DataStatus.Unknown, source.GetPublisher(null).Status);
        Assert.Equal(DataStatus.Unknown, source.GetPublisher(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe")).Status);
    }

    [Fact]
    public void File_without_version_resource_is_unknown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"booster-test-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, "not an executable");
        try
        {
            var publisher = new FilePublisherSource().GetPublisher(path);

            Assert.Equal(DataStatus.Unknown, publisher.Status);
            Assert.Equal(FilePublisherSource.Source, publisher.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
