namespace Skanyxx.Module.Identity.Tests;

/// <summary>Credential posts have their own, stricter per-IP window; reads are not counted in it.</summary>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SignIn_OverTheWindow_Is429Problem()
    {
        await postgres.ResetAsync();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "3";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });

        var bootstrap = await app.BootstrapAsync();
        var first = await app.SignInAsync(password: "wrong password 1");
        var tokens = await app.SignInBearerAsync();
        // Neither refresh nor sign-out counts: a burst of sign-ins must not stop anyone refreshing or signing out.
        var refresh = await app.RefreshAsync(tokens.RefreshToken);
        var signOut = await app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null, TestContext.Current.CancellationToken);
        var fourth = await app.SignInAsync();
        var status = await app.Client().GetAsync("/api/identity/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal("application/problem+json", fourth.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }
}
