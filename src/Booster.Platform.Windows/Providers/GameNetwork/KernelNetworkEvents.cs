using System.Buffers.Binary;
using System.Net;

namespace Booster.Platform.Windows.Providers.GameNetwork;

public readonly record struct KernelNetworkEvent(int ProcessId, int Size, IPAddress Destination, IPAddress Source);

/// <summary>События Microsoft-Windows-Kernel-Network о переданных и принятых данных, раскладка манифеста версии 0.</summary>
public static class KernelNetworkEvents
{
    public static readonly Guid ProviderId = new("7dd42a49-5329-4832-8dfd-43d979153a88");

    public const ulong Keywords = 0x30;   // KERNEL_NETWORK_KEYWORD_IPV4 | _IPV6

    private const byte ManifestVersion = 0;

    private const ushort TcpV4Send = 10;
    private const ushort TcpV4Receive = 11;
    private const ushort TcpV6Send = 26;
    private const ushort TcpV6Receive = 27;
    private const ushort UdpV4Send = 42;
    private const ushort UdpV4Receive = 43;
    private const ushort UdpV6Send = 58;
    private const ushort UdpV6Receive = 59;

    private const int SizeOffset = 4;
    private const int DestinationOffset = 8;
    private const int IPv4Length = 4;
    private const int IPv6Length = 16;

    // PID, size, daddr, saddr, dport, sport.
    private const int MinimumIPv4Length = 20;
    private const int MinimumIPv6Length = 44;

    /// <summary>Событие о переданных или принятых данных, какой бы ни была его версия.</summary>
    public static bool IsTrafficEventId(ushort eventId) => Layout(eventId).AddressLength != 0;

    public static bool TryParse(ushort eventId, byte version, ReadOnlySpan<byte> data, out KernelNetworkEvent e)
    {
        e = default;
        if (version != ManifestVersion)
            return false;

        var (addressLength, minimumLength) = Layout(eventId);
        if (addressLength == 0 || data.Length < minimumLength)
            return false;

        e = new KernelNetworkEvent(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data[SizeOffset..]),
            new IPAddress(data.Slice(DestinationOffset, addressLength)),
            new IPAddress(data.Slice(DestinationOffset + addressLength, addressLength)));
        return true;
    }

    private static (int AddressLength, int MinimumLength) Layout(ushort eventId) => eventId switch
    {
        TcpV4Send or TcpV4Receive or UdpV4Send or UdpV4Receive => (IPv4Length, MinimumIPv4Length),
        TcpV6Send or TcpV6Receive or UdpV6Send or UdpV6Receive => (IPv6Length, MinimumIPv6Length),
        _ => (0, 0),
    };
}
