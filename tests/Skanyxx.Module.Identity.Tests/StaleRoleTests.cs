using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// A role change or a disable takes effect at once on sessions already open: the cookie, the refresh token and the
/// bearer access token (A3 / SEC2 N1: both schemes check the security stamp on every request).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaleRoleTests(PostgresFixture postgres)
{
    public static TheoryData<string> Changes => new() { "roles", "disable" };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task BearerAccessToken_IsRefusedAtOnce(string change)
    {
        await using var app = await StartAsync();
        var (owner, member, memberId) = await MemberAsync(app);
        var before = await app.Client(bearer: member.AccessToken).GetAsync(IdentityApp.ProbePath);

        await ChangeAsync(app, owner, memberId, change);
        var after = await app.Client(bearer: member.AccessToken).GetAsync(IdentityApp.ProbePath);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task RefreshToken_IsRefused(string change)
    {
        await using var app = await StartAsync();
        var (owner, member, memberId) = await MemberAsync(app);

        await ChangeAsync(app, owner, memberId, change);
        var refresh = await app.RefreshAsync(member.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    /// <summary>
    /// SEC2 N1: the cookie is stamp-checked on every request, like a bearer token, so the very next request is refused
    /// with no time passing (a 60 s window used to let a demoted supervisor mint an agent secret that outlived them).
    /// </summary>
    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Cookie_IsRefusedOnTheVeryNextRequest(string change)
    {
        var clock = new ManualClock();
        await using var app = await StartAsync(clock);
        var (owner, _, memberId) = await MemberAsync(app);
        var cookie = SetCookie.AuthHeader(await app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword, useCookie: true));
        var before = await app.Client(cookie: cookie).GetAsync(IdentityApp.ProbePath);

        await ChangeAsync(app, owner, memberId, change);
        var after = await app.Client(cookie: cookie).GetAsync(IdentityApp.ProbePath);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    /// CR4 / D14: a disable committing between a sign-in's password check and its cookie leaves a cookie carrying the
    /// old stamp; the next request refuses it (it used to work until the next interval check).
    /// </summary>
    [Fact]
    public async Task Cookie_FromASignInThatRacedADisable_IsRefusedOnTheNextRequest()
    {
        var race = new SignInRace();
        await postgres.ResetAsync();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s
            .AddSingleton(race)
            .AddScoped<SignInManager<IdentityUser>, RacingSignInManager>());
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        var (owner, _, memberId) = await MemberAsync(app);
        race.BeforeCookie = () => ChangeAsync(app, owner, memberId, "disable");

        var signIn = await app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword, useCookie: true);
        var next = await app.Client(cookie: SetCookie.AuthHeader(signIn)).GetAsync(IdentityApp.ProbePath);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.True(race.Ran);
        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
    }

    /// <summary>Sign-out now reaches access tokens too (they used to run out their hour).</summary>
    [Fact]
    public async Task SignOut_RefusesTheAccessTokenAtOnce()
    {
        await using var app = await StartAsync();
        var tokens = await app.SignInBearerAsync();

        await app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null);
        var after = await app.Client(bearer: tokens.AccessToken).GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task UnchangedRoles_LeaveSessionsAlone()
    {
        await using var app = await StartAsync();
        var (owner, member, memberId) = await MemberAsync(app);

        await app.Client(bearer: owner).PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { "builder" } });
        var after = await app.Client(bearer: member.AccessToken).GetAsync(IdentityApp.ProbePath);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private async Task<IdentityApp> StartAsync(ManualClock? clock = null)
    {
        await postgres.ResetAsync();
        var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s =>
        {
            if (clock is not null)
                s.AddSingleton<TimeProvider>(clock);
        });
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        return app;
    }

    private static async Task<(string Owner, Tokens Member, string MemberId)> MemberAsync(IdentityApp app)
    {
        var owner = (await app.SignInBearerAsync()).AccessToken;
        var (member, memberId) = await app.AddMemberAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder);
        return (owner, member, memberId);
    }

    private static async Task ChangeAsync(IdentityApp app, string owner, string memberId, string change)
    {
        var response = change == "roles"
            ? await app.Client(bearer: owner).PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { "employee" } })
            : await app.Client(bearer: owner).PostAsync($"/api/identity/people/{memberId}/disable", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
