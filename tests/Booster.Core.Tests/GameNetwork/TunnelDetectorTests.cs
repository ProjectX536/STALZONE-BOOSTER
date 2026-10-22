using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.GameNetwork;

public class TunnelDetectorTests
{
    private static readonly string[] Keywords = ["wireguard", "openvpn", "tap-windows"];

    private static NetworkAdapterInfo Adapter(string name, string description, NetworkMedium medium) =>
        new("id", name, description, medium, null, []);

    [Fact]
    public void TunnelMedium_IsTunnelWithoutKeywords() =>
        Assert.True(TunnelDetector.IsTunnel(Adapter("Ethernet 3", "Generic", NetworkMedium.Tunnel), []));

    [Fact]
    public void DescriptionKeyword_IsTunnel() =>
        Assert.True(TunnelDetector.IsTunnel(Adapter("Ethernet 3", "WireGuard Tunnel", NetworkMedium.Other), Keywords));

    [Fact]
    public void NameKeyword_IsTunnel() =>
        Assert.True(TunnelDetector.IsTunnel(Adapter("OpenVPN Data", "Generic", NetworkMedium.Other), Keywords));

    [Fact]
    public void Keyword_IgnoresCase() =>
        Assert.True(TunnelDetector.IsTunnel(Adapter("x", "TAP-WINDOWS adapter V9", NetworkMedium.Ethernet), Keywords));

    [Fact]
    public void PlainWifiAdapter_IsNotTunnel() =>
        Assert.False(TunnelDetector.IsTunnel(Adapter("Wi-Fi", "Intel(R) Wi-Fi 6 AX201", NetworkMedium.Wireless), Keywords));
}
