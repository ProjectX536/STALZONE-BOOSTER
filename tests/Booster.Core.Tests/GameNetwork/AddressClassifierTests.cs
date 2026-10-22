using System.Net;
using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.GameNetwork;

public class AddressClassifierTests
{
    [Theory]
    [InlineData("10.1.2.3", AddressScope.Private)]
    [InlineData("172.16.0.1", AddressScope.Private)]
    [InlineData("192.168.1.1", AddressScope.Private)]
    [InlineData("172.32.0.1", AddressScope.Public)]
    [InlineData("100.64.0.1", AddressScope.Cgnat)]
    [InlineData("100.127.255.254", AddressScope.Cgnat)]
    [InlineData("100.128.0.1", AddressScope.Public)]
    [InlineData("127.0.0.1", AddressScope.Loopback)]
    [InlineData("169.254.1.1", AddressScope.LinkLocal)]
    [InlineData("224.0.0.251", AddressScope.Multicast)]
    [InlineData("0.0.0.0", AddressScope.Unspecified)]
    [InlineData("255.255.255.255", AddressScope.Reserved)]
    [InlineData("240.0.0.1", AddressScope.Reserved)]
    [InlineData("203.0.113.5", AddressScope.Public)]
    [InlineData("::1", AddressScope.Loopback)]
    [InlineData("fe80::1", AddressScope.LinkLocal)]
    [InlineData("fd00::1", AddressScope.Private)]
    [InlineData("ff02::1", AddressScope.Multicast)]
    [InlineData("::", AddressScope.Unspecified)]
    [InlineData("2001:db8::1", AddressScope.Public)]
    [InlineData("::ffff:192.168.1.1", AddressScope.Private)]
    public void Classify_ReturnsScope(string text, AddressScope expected)
    {
        var address = IPAddress.Parse(text);

        Assert.Equal(expected, AddressClassifier.Classify(address));
        Assert.Equal(expected == AddressScope.Public, AddressClassifier.IsPublic(address));
    }
}
