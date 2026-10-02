using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Tests;

/// <summary>SEC2-N3: "direct loopback" means a loopback peer, no forwarding headers, and a loopback Host (not a rebound name).</summary>
public sealed class DirectLoopbackTests
{
    [Theory]
    [InlineData("127.0.0.1", "localhost:5282")]
    [InlineData("127.0.0.1", "LOCALHOST")]
    [InlineData("127.0.0.1", "127.0.0.1:5282")]
    [InlineData("::1", "[::1]:5282")]
    public void LoopbackPeer_LoopbackHost_Counts(string peer, string host) => Assert.True(DirectLoopback.Is(Context(peer, host)));

    [Theory]
    [InlineData("127.0.0.1", "evil.test")]
    [InlineData("127.0.0.1", "evil.test:5282")]
    [InlineData("127.0.0.1", "localhost.evil.test")]
    [InlineData("127.0.0.1", "127.0.0.1.nip.io")]
    [InlineData("::1", "[2001:db8::1]:5282")]
    [InlineData("203.0.113.9", "localhost")]
    public void ForeignHostOrPeer_DoesNotCount(string peer, string host) => Assert.False(DirectLoopback.Is(Context(peer, host)));

    [Fact]
    public void ForwardedRequest_DoesNotCount()
    {
        var context = Context("127.0.0.1", "localhost");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        Assert.False(DirectLoopback.Is(context));
    }

    private static DefaultHttpContext Context(string peer, string host)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Host = HostString.FromUriComponent(host);
        return context;
    }
}
