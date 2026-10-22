using System.Net.NetworkInformation;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class NetworkAdapterSourceTests
{
    [Fact]
    public void Medium_maps_tunnel()
    {
        Assert.Equal(NetworkMedium.Tunnel, NetworkAdapterSource.Medium(NetworkInterfaceType.Tunnel));
        Assert.Equal(NetworkMedium.Other, NetworkAdapterSource.Medium(NetworkInterfaceType.Ppp));
    }
}
