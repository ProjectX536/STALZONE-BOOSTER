using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Booster.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WinSock;

namespace Booster.Platform.Windows.Providers.GameNetwork;

public sealed unsafe class RouteSource : IRouteSource
{
    private const string Source = "GetBestInterfaceEx";

    public Reading<string> AdapterIdFor(IPAddress destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        try
        {
            var (index, error) = BestInterfaceIndex(destination);
            if (index is null)
                return error is WIN32_ERROR.ERROR_NETWORK_UNREACHABLE or WIN32_ERROR.ERROR_HOST_UNREACHABLE or WIN32_ERROR.ERROR_BAD_NETPATH
                    ? Reading<string>.Unknown(Source, $"no route ({(uint)error})")
                    : Reading<string>.Failure(Source, $"GetBestInterfaceEx failed ({(uint)error})");

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (HasIndex(adapter, index.Value))
                    return Reading<string>.Known(adapter.Id, Source);
            }

            return Reading<string>.Unknown(Source, "interface index has no matching adapter");
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return Reading<string>.Failure(Source, ex.Message);
        }
    }

    private static (uint? Index, WIN32_ERROR Error) BestInterfaceIndex(IPAddress destination)
    {
        // sockaddr_in (16 bytes) / sockaddr_in6 (28 bytes): family, port, address; sin6_scope_id at offset 24; the rest stays zero.
        var ipv6 = destination.AddressFamily == AddressFamily.InterNetworkV6;
        Span<byte> sockaddr = stackalloc byte[ipv6 ? 28 : 16];
        sockaddr[0] = (byte)destination.AddressFamily;
        if (!destination.TryWriteBytes(sockaddr[(ipv6 ? 8 : 4)..], out _))
            return (null, WIN32_ERROR.ERROR_INVALID_PARAMETER);
        if (ipv6)
            BitConverter.TryWriteBytes(sockaddr[24..], (uint)destination.ScopeId);

        uint index;
        fixed (byte* pointer = sockaddr)
        {
            var error = (WIN32_ERROR)PInvoke.GetBestInterfaceEx((SOCKADDR*)pointer, &index);
            return error == WIN32_ERROR.ERROR_SUCCESS ? (index, error) : (null, error);
        }
    }

    private static bool HasIndex(NetworkInterface adapter, uint index)
    {
        var properties = adapter.GetIPProperties();
        return TryIndex(() => properties.GetIPv4Properties().Index, index) || TryIndex(() => properties.GetIPv6Properties().Index, index);
    }

    private static bool TryIndex(Func<int> read, uint index)
    {
        try
        {
            return read() == index;
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
