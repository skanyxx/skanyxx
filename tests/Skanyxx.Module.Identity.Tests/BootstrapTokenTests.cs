namespace Skanyxx.Module.Identity.Tests;

/// <summary>With <c>Identity:BootstrapToken</c> set, only the token counts — loopback alone is not enough.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BootstrapTokenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Token = IdentityApp.BootstrapToken;
    private IdentityApp _app = null!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:BootstrapToken"] = Token);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData(Token + "x")]
    [InlineData("a-bootstrap-token-of-at-least-32-character")]
    public async Task MissingOrWrongToken_Is403(string? token)
    {
        var response = await _app.BootstrapAsync(token: token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await postgres.UserCountAsync());
    }

    [Fact]
    public async Task RightToken_Is201()
    {
        var response = await _app.BootstrapAsync(token: Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
