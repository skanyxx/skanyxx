using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Skanyxx.Module.Identity.Tests;

public sealed class SignInTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
    }

    [Fact]
    public async Task Bearer_SignIn_ReturnsTokens_ThatAuthenticateMe()
    {
        var tokens = await App.SignInBearerAsync();

        var me = await App.Client(bearer: tokens.AccessToken).GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3600, tokens.ExpiresIn);
        Assert.Equal(IdentityApp.OwnerEmail, me.GetProperty("email").GetString());
        Assert.Equal("The Owner", me.GetProperty("displayName").GetString());
        Assert.Equal(["owner"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Bearer_CallerIsTheUser_AndOwnerIsSupervisor()
    {
        var tokens = await App.SignInBearerAsync();
        var me = await App.Client(bearer: tokens.AccessToken).GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken);

        var probe = await App.Client(bearer: tokens.AccessToken).GetFromJsonAsync<JsonElement>(IdentityApp.ProbePath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(me.GetProperty("id").GetString(), probe.GetProperty("userId").GetString());
        Assert.True(probe.GetProperty("supervisor").GetBoolean());
    }

    [Fact]
    public async Task Cookie_SignIn_SetsHttpOnlyLaxCookie_ThatAuthenticatesMe()
    {
        var response = await App.SignInAsync(useCookie: true);
        var cookie = SetCookie.Auth(response);

        var me = await App.Client(cookie: SetCookie.AuthHeader(response)).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.Null(cookie.Expires); // session cookie: not persisted past the browser session
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Cookie_SignIn_ReturnsNoTokens()
    {
        var response = await App.SignInAsync(useCookie: true);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("tokens").ValueKind);
    }

    [Fact]
    public async Task Bearer_SignIn_SetsNoCookie()
    {
        var response = await App.SignInAsync();

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task EmailIsCaseInsensitive()
    {
        var response = await App.SignInAsync(IdentityApp.OwnerEmail.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEmail_AndWrongPassword_AreIndistinguishable()
    {
        var unknown = await App.SignInAsync("nobody@skanyxx.example", IdentityApp.OwnerPassword);
        var wrong = await App.SignInAsync(IdentityApp.OwnerEmail, "not the password at all");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(await WithoutTraceIdAsync(unknown), await WithoutTraceIdAsync(wrong));
        Assert.Contains("Invalid email or password.", await WithoutTraceIdAsync(unknown));
    }

    [Fact]
    public async Task SignOut_Cookie_ExpiresTheCookie()
    {
        var signIn = await App.SignInAsync(useCookie: true);
        var client = App.Client(cookie: SetCookie.AuthHeader(signIn));

        var signOut = await client.PostAsync("/api/identity/sign-out", null, TestContext.Current.CancellationToken);
        var cleared = SetCookie.Auth(signOut);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal("", cleared.Value.ToString());
        Assert.True(cleared.Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SignOut_WithoutAuthentication_Is401()
    {
        var response = await App.Client().PostAsync("/api/identity/sign-out", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync())!;
        json.Remove("traceId");
        return JsonSerializer.Serialize(json);
    }
}
