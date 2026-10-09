using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D087 as hardened in QA round 1: the stamp is checked on the token the handler will authenticate, wherever an event
/// found it (CR1 / SEC L3), and a token the handler refuses anyway costs no database read (CR obs 7).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BearerStampTests(PostgresFixture postgres)
{
    [Fact]
    public async Task TokenSetByAnEarlierEvent_IsStampChecked()
    {
        await postgres.ResetAsync();
        // An event supplying the token from the query string, as SignalR or SSE clients would need. The request still
        // carries an Authorization header (another scheme), which is what routes it to the bearer handler.
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s =>
            s.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o => o.Events.OnMessageReceived = context =>
            {
                context.Token = context.Request.Query["access_token"];
                return Task.CompletedTask;
            }));
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        var tokens = await app.SignInBearerAsync();
        var path = $"{IdentityApp.ProbePath}?access_token={Uri.EscapeDataString(tokens.AccessToken)}";

        var before = await QueryClient(app).GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null, TestContext.Current.CancellationToken)).StatusCode);
        var after = await QueryClient(app).GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private static HttpClient QueryClient(IdentityApp app)
    {
        var client = app.Client();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Query");
        return client;
    }

    [Fact]
    public async Task ExpiredToken_IsRefusedWithoutAStampCheck()
    {
        await postgres.ResetAsync();
        var clock = new ManualClock();
        var checks = new StampChecks();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton(checks)
            .AddScoped<SignInManager<IdentityUser>, CountingSignInManager>());
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        var tokens = await app.SignInBearerAsync();

        var live = await app.Client(bearer: tokens.AccessToken).GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);
        var checksWhileLive = checks.Count;
        clock.Advance(TimeSpan.FromSeconds(tokens.ExpiresIn + 1));
        var expired = await app.Client(bearer: tokens.AccessToken).GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(1, checksWhileLive);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(1, checks.Count);
    }
}
