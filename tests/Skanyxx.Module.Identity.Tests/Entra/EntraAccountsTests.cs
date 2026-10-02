using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>D11 below the page: even a link that got past the challenge never binds a Microsoft login to the owner.</summary>
public sealed class EntraAccountsTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Fact]
    public async Task TheOwner_IsNeverLinked()
    {
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        await using var db = Postgres.CreateDbContext();
        var ownerId = db.Users.Single().Id;
        await using var scope = App.Services.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<EntraAccounts>().LinkAsync(ownerId,
            "11111111-1111-1111-1111-111111111111|aaaaaaaa-0000-0000-0000-000000000001",
            new EntraMapping(EntraAccountKind.Member, 1, [SkanyxxRoles.Supervisor], []), CancellationToken.None);

        Assert.Equal(OutcomeStatus.Forbidden, outcome.Status);
        Assert.Equal(EntraAccounts.OwnerStaysLocal, outcome.Message);
        Assert.False(db.UserLogins.Any());
    }
}
