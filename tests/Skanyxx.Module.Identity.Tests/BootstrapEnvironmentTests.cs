using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC S2 / CR M2: outside Development the bootstrap token is the only way in. A mesh sidecar (Istio's 127.0.0.6), a
/// port-forward or a proxy that only sets <c>X-Real-IP</c> all arrive from loopback, so loopback proves nothing there.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BootstrapEnvironmentTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("127.0.0.1", null)]
    [InlineData("127.0.0.6", null)]           // Istio inbound passthrough
    [InlineData("127.0.0.1", "203.0.113.9")]  // a proxy that sets only X-Real-IP
    public async Task Production_WithoutToken_IsRefused(string peer, string? realIp)
    {
        await using var app = await StartAsync("Production", peer);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/bootstrap")
        {
            Content = JsonContent.Create(new { email = IdentityApp.OwnerEmail, password = IdentityApp.OwnerPassword })
        };
        if (realIp is not null)
            request.Headers.Add("X-Real-IP", realIp);

        var response = await app.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Identity:BootstrapToken", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await postgres.UserCountAsync());
    }

    [Fact]
    public async Task Production_WithToken_Works()
    {
        await using var app = await StartAsync("Production", "127.0.0.6", IdentityApp.BootstrapToken);

        var response = await app.BootstrapAsync(token: IdentityApp.BootstrapToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Development_KeepsTheLoopbackFallback()
    {
        await using var app = await StartAsync("Development", "127.0.0.1");

        var response = await app.BootstrapAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// SEC2-N3: a rebound page reaches the instance from the developer's own browser (a loopback peer) but under the
    /// attacker's host name. Only a loopback Host counts, so this holds even with <c>AllowedHosts=*</c>.
    /// </summary>
    [Theory]
    [InlineData("evil.test")]
    [InlineData("evil.test:5282")]
    [InlineData("127.0.0.1.nip.io")]
    public async Task Development_LoopbackPeer_UnderAForeignHost_IsRefused(string host)
    {
        await using var app = await StartAsync("Development", "127.0.0.1");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/bootstrap")
        {
            Content = JsonContent.Create(new { email = IdentityApp.OwnerEmail, password = IdentityApp.OwnerPassword })
        };
        request.Headers.Host = host;

        var response = await app.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await postgres.UserCountAsync());
    }

    private async Task<IdentityApp> StartAsync(string environment, string peer, string? token = null)
    {
        await postgres.ResetAsync();
        return await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:BootstrapToken"] = token,
            environment, services => services.AddSingleton<IStartupFilter>(new PeerAddress(IPAddress.Parse(peer))));
    }

    /// <summary>Stands in for the TCP peer, which a test on one machine cannot choose.</summary>
    private sealed class PeerAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, rest) =>
            {
                context.Connection.RemoteIpAddress = address;
                return rest(context);
            });
            next(app);
        };
    }
}
