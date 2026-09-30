using System.Net.Http.Json;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC L7: a password must not contain the email's local part (case-insensitive, local part of 3+ characters), wherever
/// Identity sets one: owner bootstrap and invite accept alike. There is no breached-password list (accepted risk, D088).
/// </summary>
public sealed class PasswordPolicyTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Fact]
    public async Task Bootstrap_WithTheLocalPartInThePassword_Is400()
    {
        var refused = await App.BootstrapAsync(password: "OWNER plus a long tail");
        var accepted = await App.BootstrapAsync();

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("password", (await refused.Content.ReadAsStringAsync()).ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task Accept_WithTheLocalPartInThePassword_Is400_AndTheInviteStaysOpen()
    {
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        var owner = (await App.SignInBearerAsync()).AccessToken;
        var token = await App.InviteAsync(owner);

        var refused = await App.AcceptAsync(token, password: "my name is Builder, hi");
        var stillOpen = await App.LookupInviteAsync(token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("\"Password\"", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
    }

    [Fact]
    public async Task ShortLocalPart_IsNotHeldAgainstThePassword()
    {
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        var owner = (await App.SignInBearerAsync()).AccessToken;
        var token = await App.InviteAsync(owner, "al@skanyxx.example");

        var accepted = await App.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = "totally normal password" });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }
}
