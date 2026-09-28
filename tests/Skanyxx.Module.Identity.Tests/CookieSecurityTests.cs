namespace Skanyxx.Module.Identity.Tests;

/// <summary>The auth cookie is Secure everywhere except Development (plain-http local runs).</summary>
[Collection(PostgresCollection.Name)]
public sealed class CookieSecurityTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public async Task SecureFlag_FollowsEnvironment(string environment, bool secure)
    {
        await postgres.ResetAsync();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString,
            s => s["Identity:BootstrapToken"] = IdentityApp.BootstrapToken, environment);
        await app.BootstrapAsync(token: IdentityApp.BootstrapToken);

        var response = await app.SignInAsync(useCookie: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(secure, SetCookie.Auth(response).Secure);
        Assert.True(SetCookie.Auth(response).HttpOnly);
    }
}
