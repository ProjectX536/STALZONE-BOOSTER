using System.Text;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.GameNetwork;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class GameNetworkReportViewTests
{
    private static GameNetworkReportView View(GameNetworkMeasurement? measurement = null, LoadTestResult? load = null,
        GameNetworkComparison? comparison = null)
    {
        measurement ??= GameNetworkSamples.Measurement();
        return GameNetworkReportView.From(measurement, GameNetworkSamples.Analysis(measurement), load, comparison);
    }

    private static async Task<string> ExportAsync(IReportExporter exporter, SystemReport report)
    {
        using var stream = new MemoryStream();
        await exporter.ExportAsync(report, stream, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void View_keeps_targets_and_route_without_addresses()
    {
        var view = View();

        Assert.Equal([NetworkTargetKind.Gateway, NetworkTargetKind.IspHop, NetworkTargetKind.Host], view.Targets.Select(t => t.Kind));
        Assert.Equal(40, view.Targets[2].MedianMs.Value);
        Assert.Equal(140, view.Targets[2].P95Ms.Value);
        Assert.Equal([1, 2, 3, 4], view.Route.Select(h => h.Ttl));
        Assert.Equal(AddressScope.Cgnat, view.Route[1].Scope);
        Assert.Equal(12, view.Route[1].MedianMs.Value);
        Assert.Equal(new RouteJump(2, 3, 24), view.Jump);
        Assert.Equal(GameNetworkSamples.Server, view.Server);
        Assert.Equal(ServerSource.Etw, view.ServerSource);
        Assert.Equal(NetworkMedium.Wireless, view.Medium);
        Assert.Equal(866_000_000, view.LinkSpeedBitsPerSecond);
        Assert.Equal(MeasurementEnd.Completed, view.End);
    }

    [Fact]
    public void Advice_adds_load_advice()
    {
        Assert.DoesNotContain(View().Advice, a => a.Id == AdviceId.GameNetworkBufferbloat);
        Assert.Contains(View(load: GameNetworkSamples.Load()).Advice, a => a.Id == AdviceId.GameNetworkBufferbloat);
    }

    [Fact]
    public void Consumers_keep_name_kind_and_rate()
    {
        var consumer = Assert.Single(View().Consumers.Value!);

        Assert.Equal(new ConsumerView("steam", NetworkConsumerKind.Downloader, 350.5), consumer);
    }

    [Fact]
    public void Unavailable_consumers_keep_status_and_note()
    {
        var measurement = GameNetworkSamples.Measurement(consumers: Reading<IReadOnlyList<ConsumerTraffic>>.NeedsAdmin("etw", "admin"));

        var consumers = View(measurement).Consumers;

        Assert.Equal(DataStatus.RequiresAdmin, consumers.Status);
        Assert.Equal("etw", consumers.Source);
        Assert.Equal("admin", consumers.Note);
    }

    [Fact]
    public async Task Report_has_no_hop_or_gateway_addresses()
    {
        // Раздел «Сеть» этапа 5 показывает шлюзы адаптеров сам по себе; здесь проверяется только раздел «Сеть в игре».
        var healthy = Healthy();
        var snapshot = healthy with
        {
            System = healthy.System with
            {
                NetworkAdapters = Reading<IReadOnlyList<NetworkAdapterInfo>>.Known(
                    [FakeNetworkAdapters.Adapter("eth0", NetworkMedium.Ethernet, 1_000_000_000)], "test"),
            },
        };
        var report = Report(snapshot, gameNetwork: View(load: GameNetworkSamples.Load(), comparison: GameNetworkSamples.Comparison()));
        var configuration = new FakeConfiguration();

        var json = await ExportAsync(new JsonReportExporter(), report);
        var html = await ExportAsync(new HtmlReportExporter(new ReportDocumentBuilder(new OfficialSourceResolver(configuration), configuration)),
            report);

        foreach (var text in new[] { json, html })
        {
            Assert.DoesNotContain(GameNetworkSamples.MiddleHop, text, StringComparison.Ordinal);
            Assert.DoesNotContain(GameNetworkSamples.IspHop, text, StringComparison.Ordinal);
            Assert.DoesNotContain(GameNetworkSamples.Gateway, text, StringComparison.Ordinal);
            Assert.Contains(GameNetworkSamples.Server, text, StringComparison.Ordinal);
        }
    }
}
