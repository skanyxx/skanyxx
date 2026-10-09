namespace Skanyxx.Module.Identity.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class IdentityTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;
    protected IdentityApp App { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        await Postgres.ResetAsync();
        App = await IdentityApp.StartAsync(Postgres.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
