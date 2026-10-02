using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>Break-glass: whoever holds the bootstrap token can clear the owner's lockout, and nobody else.</summary>
[Collection(PostgresCollection.Name)]
public sealed class UnlockTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IdentityApp _app = null!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:BootstrapToken"] = IdentityApp.BootstrapToken);
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync(token: IdentityApp.BootstrapToken)).StatusCode);
        for (var i = 0; i < 5; i++)
            await _app.SignInAsync(password: "wrong password " + i);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task RightToken_UnlocksTheOwner()
    {
        var locked = await _app.SignInAsync();

        var unlock = await _app.UnlockAsync();
        var signIn = await _app.SignInAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData(IdentityApp.BootstrapToken + "x")]
    public async Task MissingOrWrongToken_Is401_AndStaysLocked(string? token)
    {
        var unlock = await _app.UnlockAsync(token: token);

        Assert.Equal(HttpStatusCode.Unauthorized, unlock.StatusCode);
        Assert.True((await postgres.OwnerAsync()).LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task UnknownEmail_Is404()
    {
        var unlock = await _app.UnlockAsync("nobody@skanyxx.example");

        Assert.Equal(HttpStatusCode.NotFound, unlock.StatusCode);
    }

    [Fact]
    public async Task NonOwner_Is404_AndStaysLocked()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var member = new IdentityUser { UserName = "member@skanyxx.example", Email = "member@skanyxx.example" };
            Assert.True((await users.CreateAsync(member, "a colleague's long password")).Succeeded);
            Assert.True((await users.SetLockoutEndDateAsync(member, DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
        }

        var unlock = await _app.UnlockAsync("member@skanyxx.example");

        Assert.Equal(HttpStatusCode.NotFound, unlock.StatusCode);
        await using var db = postgres.CreateDbContext();
        Assert.True(db.Users.Single(u => u.Email == "member@skanyxx.example").LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"email":null}""")]
    [InlineData("""{"email":"owner\u0000@skanyxx.example"}""")]
    public async Task MissingOrControlCharacterEmail_Is400(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/unlock")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Bootstrap-Token", IdentityApp.BootstrapToken);

        var response = await _app.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
