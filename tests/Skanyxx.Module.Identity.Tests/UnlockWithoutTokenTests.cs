namespace Skanyxx.Module.Identity.Tests;

/// <summary>Without a bootstrap token there is no break-glass door at all.</summary>
[Collection(PostgresCollection.Name)]
public sealed class UnlockWithoutTokenTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Fact]
    public async Task NoConfiguredToken_Is404()
    {
        await App.BootstrapAsync();

        var unlock = await App.UnlockAsync(token: "anything-at-all");

        Assert.Equal(HttpStatusCode.NotFound, unlock.StatusCode);
    }
}
