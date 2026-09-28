using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC S3: sessions end server-side. Sign-out kills copied cookies (at the next stamp check), refresh tokens are
/// single-use with reuse revoking the chain, and cookie and refresh chain both stop at the absolute session cap.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SessionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CopiedCookie_IsRejected_AfterSignOut()
    {
        await using var app = await StartAsync();
        var cookie = SetCookie.AuthHeader(await app.SignInAsync(useCookie: true));

        var signOut = await app.Client(cookie: cookie).PostAsync("/api/identity/sign-out", null);
        var replay = await app.Client(cookie: cookie).GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task BearerSignOut_AlsoEndsTheUsersCookieSessions()
    {
        await using var app = await StartAsync();
        var cookie = SetCookie.AuthHeader(await app.SignInAsync(useCookie: true));
        var tokens = await app.SignInBearerAsync();

        await app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null);
        var browser = await app.Client(cookie: cookie).GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, browser.StatusCode);
    }

    [Fact]
    public async Task StampCheck_DefaultsToOneMinute()
    {
        await using var app = await StartAsync(stampSeconds: null);

        Assert.Equal(TimeSpan.FromMinutes(1), app.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval);
    }

    [Fact]
    public async Task RefreshToken_IsSingleUse_AndReuseRevokesTheChain()
    {
        await using var app = await StartAsync();
        var first = await app.SignInBearerAsync();

        var second = await app.RefreshAsync(first.RefreshToken);
        var next = await TokensAsync(second);
        var reuse = await app.RefreshAsync(first.RefreshToken);
        var afterReuse = await app.RefreshAsync(next.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode); // the chain died with the reuse
    }

    [Fact]
    public async Task RefreshChain_StopsAtTheSessionCap_FromTheOriginalSignIn()
    {
        var clock = new ManualClock();
        await using var app = await StartAsync(clock, sessionDays: 1);
        var tokens = await app.SignInBearerAsync();

        clock.Advance(TimeSpan.FromHours(20));
        var early = await app.RefreshAsync(tokens.RefreshToken);
        var next = await TokensAsync(early);
        clock.Advance(TimeSpan.FromHours(5)); // 25 h after sign-in; the token itself is 5 h old
        var late = await app.RefreshAsync(next.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
    }

    /// <summary>SEC2-N4: an access token issued near the cap expires with the chain, not an hour later.</summary>
    [Fact]
    public async Task AccessToken_IssuedNearTheCap_ExpiresWithTheChain()
    {
        var clock = new ManualClock();
        await using var app = await StartAsync(clock, sessionDays: 1);
        var tokens = await app.SignInBearerAsync();

        clock.Advance(TimeSpan.FromHours(23.5)); // 30 min left on the chain; a full access token would run 1 h
        var refreshed = await TokensAsync(await app.RefreshAsync(tokens.RefreshToken));
        clock.Advance(TimeSpan.FromMinutes(29));
        var beforeCap = await app.Client(bearer: refreshed.AccessToken).GetAsync("/api/identity/me");
        clock.Advance(TimeSpan.FromMinutes(2));
        var pastCap = await app.Client(bearer: refreshed.AccessToken).GetAsync("/api/identity/me");

        Assert.InRange(refreshed.ExpiresIn, 1, 30 * 60);
        Assert.Equal(HttpStatusCode.OK, beforeCap.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, pastCap.StatusCode);
    }

    /// <summary>
    /// CR2-m1, pinned on purpose: two refreshes racing with one token look exactly like a stolen copy being used, so
    /// one wins and the chain is then revoked, the winner's new token included. Clients must single-flight refresh.
    /// </summary>
    [Fact]
    public async Task ParallelRefresh_WithOneToken_RevokesTheChain()
    {
        await using var app = await StartAsync();
        var tokens = await app.SignInBearerAsync();

        var responses = await Task.WhenAll(app.RefreshAsync(tokens.RefreshToken), app.RefreshAsync(tokens.RefreshToken));
        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var afterRace = await app.RefreshAsync((await TokensAsync(winner)).RefreshToken);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRace.StatusCode);
    }

    [Fact]
    public async Task Cookie_StopsAtTheSessionCap_HoweverActivelyUsed()
    {
        var clock = new ManualClock();
        await using var app = await StartAsync(clock, sessionDays: 1);
        var cookie = SetCookie.AuthHeader(await app.SignInAsync(useCookie: true));

        var statuses = new List<HttpStatusCode>();
        for (var hour = 7; hour <= 28; hour += 7)
        {
            clock.Advance(TimeSpan.FromHours(7));
            var response = await app.Client(cookie: cookie).GetAsync("/api/identity/me");
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.OK && response.Headers.Contains("Set-Cookie"))
                cookie = SetCookie.AuthHeader(response);
        }

        // 7 h, 14 h, 21 h: renewed each time; 28 h: past the one-day cap.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Unauthorized], statuses);
    }

    private async Task<IdentityApp> StartAsync(ManualClock? clock = null, int sessionDays = 7, int? stampSeconds = 0)
    {
        await postgres.ResetAsync();
        var app = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Identity:SessionDays"] = sessionDays.ToString();
            if (stampSeconds is not null)
                s["Identity:SecurityStampValidationSeconds"] = stampSeconds.ToString();
        }, services: s =>
        {
            if (clock is not null)
                s.AddSingleton<TimeProvider>(clock);
        });
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        return app;
    }

    private static async Task<Tokens> TokensAsync(HttpResponseMessage response) =>
        Tokens.From(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
}
