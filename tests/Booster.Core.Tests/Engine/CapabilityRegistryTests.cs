using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;

namespace Booster.Core.Tests.Engine;

public class CapabilityRegistryTests
{
    private static CapabilityReport Report(string id, string scope = CapabilityScopes.System,
        CapabilityStatus status = CapabilityStatus.Supported, string provider = "test") =>
        new(id, scope, status, provider, null);

    [Fact]
    public void Unregistered_capability_is_unknown_from_no_provider()
    {
        var registry = new CapabilityRegistry();

        var report = registry.Get(CapabilityIds.FrameCapture);

        Assert.Equal(CapabilityStatus.Unknown, report.Status);
        Assert.Equal("none", report.ProviderId);
        Assert.Equal(CapabilityIds.FrameCapture, report.CapabilityId);
        Assert.Equal(CapabilityScopes.System, report.Scope);
    }

    [Fact]
    public void Get_returns_reported_entry()
    {
        var registry = new CapabilityRegistry();
        var report = Report(CapabilityIds.DxgiAdapters);

        registry.Report(report);

        Assert.Equal(report, registry.Get(CapabilityIds.DxgiAdapters));
    }

    [Fact]
    public void Latest_report_for_same_key_wins()
    {
        var registry = new CapabilityRegistry();
        registry.Report(Report(CapabilityIds.FrameCapture, status: CapabilityStatus.Supported));
        registry.Report(Report(CapabilityIds.FrameCapture, status: CapabilityStatus.RequiresAdmin));

        Assert.Equal(CapabilityStatus.RequiresAdmin, registry.Get(CapabilityIds.FrameCapture).Status);
        Assert.Single(registry.All());
    }

    [Fact]
    public void Scopes_are_independent()
    {
        var registry = new CapabilityRegistry();
        var adapter = CapabilityScopes.Adapter(new AdapterLuid(0xC2A1, 0));
        registry.Report(Report(CapabilityIds.HybridGpuFlags, adapter, CapabilityStatus.NotSupported));

        Assert.Equal(CapabilityStatus.NotSupported, registry.Get(CapabilityIds.HybridGpuFlags, adapter).Status);
        Assert.Equal(CapabilityStatus.Unknown, registry.Get(CapabilityIds.HybridGpuFlags).Status);
        Assert.Equal("adapter:0x00000000_0x0000C2A1", adapter);
    }

    [Fact]
    public void All_is_a_sorted_snapshot()
    {
        var registry = new CapabilityRegistry();
        registry.Report(Report("b.cap"));
        registry.Report(Report("a.cap", "adapter:2"));
        registry.Report(Report("a.cap", "adapter:1"));

        var snapshot = registry.All();
        registry.Report(Report("c.cap"));

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["a.cap/adapter:1", "a.cap/adapter:2", "b.cap/system"],
            snapshot.Select(r => $"{r.CapabilityId}/{r.Scope}"));
    }

    [Fact]
    public async Task Concurrent_reports_are_safe()
    {
        var registry = new CapabilityRegistry();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                registry.Report(Report($"cap.{(worker * 1000 + i) % 50}"));
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(50, registry.All().Count);
    }
}
