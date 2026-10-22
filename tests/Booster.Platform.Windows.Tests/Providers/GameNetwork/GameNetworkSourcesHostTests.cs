using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Platform.Windows.Providers;
using Booster.Platform.Windows.Providers.GameNetwork;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

[Trait("Category", "Host")]
public class GameNetworkSourcesHostTests
{
    [Fact]
    public void Tcp_table_reads_without_error()
    {
        var table = new TcpConnectionSource().Read();

        Assert.NotEqual(DataStatus.Failed, table.Status);
        Assert.All(table.Value ?? [], c => Assert.NotNull(c.Remote));
    }

    [Fact]
    public void Route_source_answers_for_documentation_address()
    {
        var adapter = new RouteSource().AdapterIdFor(IPAddress.Parse("192.0.2.1"));

        Assert.NotEqual(DataStatus.Failed, adapter.Status);
    }

    [Fact]
    public async Task Traceroute_loopback_is_destination()
    {
        var reply = await new TraceRouteSource().ProbeAsync(IPAddress.Loopback, 1, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(HopStatus.Destination, reply.Status);
    }

    [Fact]
    public void Wifi_source_reads_wireless_adapters()
    {
        var source = new WifiSource(() => new WlanClient(), NullLogger<WifiSource>.Instance);
        var wireless = new NetworkAdapterSource().Read().Where(a => a.Medium == NetworkMedium.Wireless);

        Assert.All(wireless, a =>
        {
            var wifi = source.Read(a.Id);
            Assert.NotEqual(DataStatus.Failed, wifi.Status);
            if (wifi.Value?.RssiDbm is { IsAvailable: true } rssi)
                Assert.InRange(rssi.Value, -100, 0);
        });
        // Без Wi-Fi-адаптера проверяет хотя бы открытие клиента WLAN и поиск интерфейса.
        Assert.NotEqual(DataStatus.Failed, source.Read(Guid.Empty.ToString()).Status);
    }
}
