using System.Buffers.Binary;
using System.Net;
using Booster.Platform.Windows.Providers.GameNetwork;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class KernelNetworkEventsTests
{
    private static readonly IPAddress Local = IPAddress.Parse("192.168.1.10");
    private static readonly IPAddress Server = IPAddress.Parse("203.0.113.5");

    [Fact]
    public void Udp_v4_send_is_parsed()
    {
        Assert.True(KernelNetworkEvents.TryParse(42, 0, Payload(1234, 120, Server, Local), out var e));

        Assert.Equal(new KernelNetworkEvent(1234, 120, Server, Local), e);
    }

    [Fact]
    public void Tcp_v6_recv_is_parsed()
    {
        var destination = IPAddress.Parse("fe80::1");
        var source = IPAddress.Parse("2001:db8::5");

        Assert.True(KernelNetworkEvents.TryParse(27, 0, Payload(4321, 1400, destination, source), out var e));

        Assert.Equal(new KernelNetworkEvent(4321, 1400, destination, source), e);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, false)]
    [InlineData(42, false)]
    [InlineData(43, false)]
    [InlineData(26, true)]
    [InlineData(27, true)]
    [InlineData(58, true)]
    [InlineData(59, true)]
    public void Every_send_and_receive_event_is_parsed(ushort eventId, bool ipv6)
    {
        var remote = ipv6 ? IPAddress.Parse("2001:db8::5") : Server;
        var local = ipv6 ? IPAddress.Parse("2001:db8::10") : Local;

        Assert.True(KernelNetworkEvents.TryParse(eventId, 0, Payload(7, 64, remote, local), out var e));

        Assert.Equal(new KernelNetworkEvent(7, 64, remote, local), e);
    }

    [Fact]
    public void Unknown_version_is_skipped()
    {
        Assert.False(KernelNetworkEvents.TryParse(42, 1, Payload(1234, 120, Server, Local), out _));
    }

    [Fact]
    public void Short_payload_is_skipped()
    {
        Assert.False(KernelNetworkEvents.TryParse(42, 0, Payload(1234, 120, Server, Local).AsSpan(0, 19), out _));
        var v6 = Payload(1234, 120, IPAddress.Parse("2001:db8::5"), IPAddress.Parse("2001:db8::10"));
        Assert.False(KernelNetworkEvents.TryParse(58, 0, v6.AsSpan(0, 43), out _));
    }

    [Fact]
    public void Unknown_event_id_is_skipped()
    {
        Assert.False(KernelNetworkEvents.TryParse(12, 0, Payload(1234, 120, Server, Local), out _));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(26, true)]
    [InlineData(27, true)]
    [InlineData(42, true)]
    [InlineData(43, true)]
    [InlineData(58, true)]
    [InlineData(59, true)]
    [InlineData(0, false)]
    [InlineData(12, false)]
    [InlineData(49, false)]
    public void Only_send_and_receive_ids_are_traffic_events(ushort eventId, bool expected)
    {
        Assert.Equal(expected, KernelNetworkEvents.IsTrafficEventId(eventId));
    }

    // PID, size, daddr, saddr, dport, sport, затем seqnum и connid, как в манифесте.
    private static byte[] Payload(uint processId, uint size, IPAddress destination, IPAddress source)
    {
        var daddr = destination.GetAddressBytes();
        var saddr = source.GetAddressBytes();
        var data = new byte[8 + daddr.Length + saddr.Length + 4 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(data, processId);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), size);
        daddr.CopyTo(data, 8);
        saddr.CopyTo(data, 8 + daddr.Length);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8 + daddr.Length + saddr.Length), 27015);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10 + daddr.Length + saddr.Length), 50000);
        return data;
    }
}
