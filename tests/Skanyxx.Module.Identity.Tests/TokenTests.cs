using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Module.Identity.Tests;

public sealed class TokenTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
    }

    [Fact]
    public async Task Refresh_IssuesNewTokens_ThatWork()
    {
        var first = await App.SignInBearerAsync();

        var refreshed = await RefreshAsync(first.RefreshToken);
        var tokens = Tokens.From(JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement);
        var me = await App.Client(bearer: tokens.AccessToken).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task AccessToken_IsNotARefreshToken()
    {
        var tokens = await App.SignInBearerAsync();

        var response = await RefreshAsync(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsNotAnAccessToken()
    {
        var tokens = await App.SignInBearerAsync();

        var response = await App.Client(bearer: tokens.RefreshToken).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedToken_Is401()
    {
        var tokens = await App.SignInBearerAsync();
        var tampered = tokens.AccessToken[..^4] + (tokens.AccessToken.EndsWith("AAAA") ? "BBBB" : "AAAA");

        var response = await App.Client(bearer: tampered).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BearerSignOut_RevokesRefreshTokens()
    {
        var tokens = await App.SignInBearerAsync();

        var signOut = await App.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null, TestContext.Current.CancellationToken);
        var refresh = await RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_OutlivesItsUser_IsRefused()
    {
        var tokens = await App.SignInBearerAsync();
        await Postgres.ResetAsync();

        var refresh = await RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        App.Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken });
}
