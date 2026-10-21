using Booster.Core.Abstractions;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

/// <summary>Читают сетевые адаптеры и пингуют loopback. Внешние узлы не опрашиваются.</summary>
[Trait("Category", "Host")]
public class NetworkSourcesHostTests
{
    [Fact]
    public void Adapter_source_reads_without_error()
    {
        var source = new NetworkAdapterSource();

        var adapters = source.Read();

        Assert.All(adapters, a =>
        {
            Assert.False(string.IsNullOrEmpty(a.Id));
            Assert.DoesNotContain("0.0.0.0", a.Gateways);
            Assert.NotEqual(DataStatus.Failed, source.ReadTrafficBytes(a.Id).Status);
        });
        Assert.Equal(DataStatus.Unknown, source.ReadTrafficBytes("no-such-adapter").Status);
    }

    [Fact]
    public async Task Loopback_ping_replies()
    {
        var reply = await new PingSource().PingAsync("127.0.0.1", TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
    }
}
