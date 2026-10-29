using System.Net.Http.Json;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>SEC M5: one per-IP fixed window over the new-module APIs, chat, the model step and /mcp; other routes are not counted.</summary>
[Collection(HostCollection.Name)]
public sealed class RateLimitTests(PostgresFixture fixture)
{
    [Fact]
    public async Task OverTheLimit_Is429Problem_OnlyOnGuardedRoutes()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["Skanyxx:RateLimit:PermitLimit"] = "2";
            s["Skanyxx:RateLimit:WindowSeconds"] = "600";
        });
        // The key ring and the owner live in the shared database, so the fixture host's token is valid here too
        // (and obtaining it here would spend this host's window).
        var client = host.Client((await fixture.Host.OwnerAsync()).DefaultRequestHeaders.Authorization!.Parameter);

        var first = await client.GetAsync("/api/tickets/pipelines", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("/api/memory/cards?q=x", TestContext.Current.CancellationToken);
        var third = await client.GetAsync("/api/sandboxes", TestContext.Current.CancellationToken);
        var mcp = await client.PostAsync("/mcp/memory", new StringContent("{}"), TestContext.Current.CancellationToken);
        var chat = await client.PostAsync("/api/chat", new StringContent("{}"), TestContext.Current.CancellationToken);
        var model = await client.GetAsync("/api/models", TestContext.Current.CancellationToken);
        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var studioForm = await client.PostAsync("/Studio?handler=Factory", new StringContent(""), TestContext.Current.CancellationToken); // m11: the page's forms do what api/studio does
        var studioPage = await client.GetAsync("/Studio", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.TooManyRequests, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, chat.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, model.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, studioForm.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, studioPage.StatusCode);
    }

    [Fact]
    public async Task TheDashboard_IsInTheSameWindow()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["Skanyxx:RateLimit:PermitLimit"] = "2";
            s["Skanyxx:RateLimit:WindowSeconds"] = "600";
        });
        var client = host.Client((await fixture.Host.OwnerAsync()).DefaultRequestHeaders.Authorization!.Parameter);

        var first = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);
        var third = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    /// <summary>
    /// D390: behind a trusted proxy each client (its X-Forwarded-For) has its own sign-in window; without one the header
    /// is ignored, and everyone behind the same address shares it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignInWindows_AreTheForwardedClients_OnlyBehindATrustedProxy(bool trusted)
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "1";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
            if (trusted)
                s["Skanyxx:TrustedProxies:0"] = "127.0.0.1/32";
        });

        async Task<HttpStatusCode> SignInAsync(string client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/sign-in")
            {
                Content = JsonContent.Create(new { email = "nobody@skanyxx.example", password = "wrong password here" })
            };
            request.Headers.Add("X-Forwarded-For", client);
            return (await host.Client().SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
        }

        var first = await SignInAsync("203.0.113.1");
        var again = await SignInAsync("203.0.113.1");
        var other = await SignInAsync("203.0.113.2");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first);
        Assert.Equal(HttpStatusCode.TooManyRequests, again);
        Assert.Equal(trusted, other != HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ATrustedProxyThatIsNotACidr_StopsStartup()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:TrustedProxies:0"] = "ingress-nginx"));

        Assert.Contains("Skanyxx:TrustedProxies must be CIDRs", ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>D397/D399: a range as wide as a pod network would let any pod in it name itself any client.</summary>
    [Fact]
    public async Task ATrustedProxyRangeBroaderThanASlash24_StopsStartup()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:TrustedProxies:0"] = "10.42.0.0/16"));

        Assert.Contains("Skanyxx:TrustedProxies may not be broader than /24 (IPv4) or /120 (IPv6)", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.0.0.0/0", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("10.42.0.0/16", true)] // k3s's whole default pod network
    [InlineData("10.42.3.0/23", true)]
    [InlineData("10.42.3.0/24", false)]
    [InlineData("10.42.3.17/32", false)]
    [InlineData("::/0", true)]
    [InlineData("fd00:7::/48", true)]
    [InlineData("fd00:7::/119", true)]
    [InlineData("fd00:7::/120", false)]
    [InlineData("fd00:7::5/128", false)]
    // D399: an IPv6 range reaching into the IPv4-mapped block trusts IPv4 peers, which a dual-mode socket hands over as ::ffff:a.b.c.d.
    [InlineData("::/80", true)]
    [InlineData("::/64", true)]
    [InlineData("::fffe:0:0/95", true)]
    [InlineData("::ffff:0:0/96", true)]
    [InlineData("::ffff:10.0.0.0/104", true)]
    [InlineData("::ffff:10.42.0.0/112", true)]
    [InlineData("::ffff:10.42.3.0/120", false)]
    [InlineData("::ffff:10.42.3.17/128", false)]
    public void TrustedProxyBreadth(string cidr, bool tooBroad) =>
        Assert.Equal(tooBroad, ApiGuardExtensions.TooBroad(System.Net.IPNetwork.Parse(cidr)));
}
