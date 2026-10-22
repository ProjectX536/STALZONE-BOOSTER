using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Providers;

public sealed class NetworkAdapterSource : INetworkAdapterSource
{
    private const string Source = "NetworkInterface";

    public IReadOnlyList<NetworkAdapterInfo> Read()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return interfaces
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => new NetworkAdapterInfo(n.Id, n.Name, n.Description, Medium(n.NetworkInterfaceType),
                n.Speed > 0 ? n.Speed : null, Gateways(n)))
            .ToArray();
    }

    public Reading<long> ReadTrafficBytes(string adapterId)
    {
        ArgumentNullException.ThrowIfNull(adapterId);
        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == adapterId);
            if (adapter is null)
                return Reading<long>.Unknown(Source, "adapter not found");

            var statistics = adapter.GetIPStatistics();
            return Reading<long>.Known(statistics.BytesReceived + statistics.BytesSent, Source);
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return Reading<long>.Failure(Source, ex.Message);
        }
    }

    public static NetworkMedium Medium(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NetworkMedium.Wireless,
        NetworkInterfaceType.Tunnel => NetworkMedium.Tunnel,
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet => NetworkMedium.Ethernet,
        _ => NetworkMedium.Other,
    };

    private static string[] Gateways(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .Select(a => a.ToString())
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
