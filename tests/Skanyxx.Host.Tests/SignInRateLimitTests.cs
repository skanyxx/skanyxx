using System.Net.Http.Json;

namespace Skanyxx.Host.Tests;

/// <summary>Password guessing is capped per IP in its own window, through the API and through the login form alike.</summary>
[Collection(HostCollection.Name)]
public sealed class SignInRateLimitTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ApiAndFormPosts_ShareTheSignInWindow_ReadsDoNot()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "2";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });
        var credentials = new { email = HostApp.OwnerEmail, password = "wrong password" };

        var api = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var form = await host.Client().PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var third = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var setupForm = await host.Client().PostAsync("/Setup", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var status = await host.Client().GetAsync("/api/identity/status");

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, form.StatusCode); // no antiforgery token, but it still counted
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, setupForm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    /// <summary>
    /// SEC L8: the invite accept page (GET too) and the anonymous invite API share their own window, so link unfurlers
    /// cannot spend the sign-in window, and a spent sign-in window leaves invites working.
    /// </summary>
    [Fact]
    public async Task InvitePageAndApi_AreCountedInTheirOwnWindow()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["Skanyxx:InviteRateLimit:PermitLimit"] = "3";
            s["Skanyxx:InviteRateLimit:WindowSeconds"] = "600";
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "1";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });
        var credentials = new { email = HostApp.OwnerEmail, password = "wrong password" };

        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var signInLimited = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var page = await host.Client().GetAsync("/Invite?token=skx_inv_guess");
        var lookup = await host.Client().PostAsJsonAsync("/api/identity/invites/lookup", new { token = "skx_inv_guess" });
        var accept = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token = "skx_inv_guess", password = "a long enough password" });
        var fourth = await host.Client().GetAsync("/Invite?token=skx_inv_guess");
        var status = await host.Client().GetAsync("/api/identity/status");

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, signInLimited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }
}
