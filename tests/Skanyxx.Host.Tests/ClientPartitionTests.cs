using System.Net;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>SEC S7: rate-limit windows are per IPv4 address but per IPv6 /64, so one subscriber's prefix is one client.</summary>
public sealed class ClientPartitionTests
{
    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2:ffff:ffff:ffff:ffff")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2:abcd::9")]
    [InlineData("203.0.113.9", "::ffff:203.0.113.9")]
    public void SameClient(string a, string b) =>
        Assert.Equal(ClientPartition.Key(IPAddress.Parse(a)), ClientPartition.Key(IPAddress.Parse(b)));

    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:3::1")]
    [InlineData("203.0.113.9", "203.0.113.10")]
    public void DifferentClients(string a, string b) =>
        Assert.NotEqual(ClientPartition.Key(IPAddress.Parse(a)), ClientPartition.Key(IPAddress.Parse(b)));

    [Fact]
    public void IPv4_IsTheAddress() => Assert.Equal("203.0.113.9", ClientPartition.Key(IPAddress.Parse("203.0.113.9")));
}
