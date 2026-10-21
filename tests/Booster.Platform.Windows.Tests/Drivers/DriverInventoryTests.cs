using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Drivers;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Drivers;

public class DriverInventoryTests
{
    [Fact]
    public async Task Two_queries_name_only_the_properties_the_mapper_reads()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia");

        var result = await new DriverInventory(cim).ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(22, result.Devices.Count);
        Assert.Equal(2, cim.Queries.Count);
        Assert.All(cim.Queries, q =>
        {
            Assert.Equal(WmiNamespaces.CimV2, q.Namespace);
            Assert.DoesNotContain("*", q.Wql, StringComparison.Ordinal);
        });
        Assert.Contains(cim.Queries, q => q.Wql.EndsWith("FROM Win32_PnPSignedDriver", StringComparison.Ordinal));
        Assert.Contains(cim.Queries, q => q.Wql.EndsWith("FROM Win32_PnPEntity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_query_propagates_for_the_section_runner()
    {
        var cim = FakeCimClient.FromFixture("desktop-nvidia").Throw("Win32_PnPSignedDriver", new InvalidOperationException("WMI broken"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DriverInventory(cim).ScanAsync(TestContext.Current.CancellationToken));

        Assert.Equal("WMI broken", error.Message);
    }
}
