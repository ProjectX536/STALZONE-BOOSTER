using System.Net;
using System.Net.Sockets;

namespace Booster.Core.GameNetwork;

public enum AddressScope { Public, Private, Cgnat, Loopback, LinkLocal, Multicast, Unspecified, Reserved }

public static class AddressClassifier
{
    public static bool IsPublic(IPAddress address) => Classify(address) == AddressScope.Public;

    public static AddressScope Classify(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return AddressScope.Loopback;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return AddressScope.Unspecified;

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork ? ClassifyV4(bytes) : ClassifyV6(address, bytes);
    }

    private static AddressScope ClassifyV4(byte[] b) => b switch
    {
        [10, ..] => AddressScope.Private,
        [172, >= 16 and <= 31, ..] => AddressScope.Private,
        [192, 168, ..] => AddressScope.Private,
        [100, >= 64 and <= 127, ..] => AddressScope.Cgnat,
        [169, 254, ..] => AddressScope.LinkLocal,
        [>= 224 and <= 239, ..] => AddressScope.Multicast,
        [>= 240, ..] => AddressScope.Reserved,
        [0, ..] => AddressScope.Reserved,
        _ => AddressScope.Public,
    };

    private static AddressScope ClassifyV6(IPAddress address, byte[] b)
    {
        if (address.IsIPv6LinkLocal)
            return AddressScope.LinkLocal;
        if (address.IsIPv6Multicast)
            return AddressScope.Multicast;
        if ((b[0] & 0xFE) == 0xFC)
            return AddressScope.Private;
        return AddressScope.Public;
    }
}
