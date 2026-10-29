namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class InvestigateTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;
    protected InvestigateApp App { get; private set; } = null!;
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual void Configure(Dictionary<string, string?> settings)
    {
    }

    public async ValueTask InitializeAsync()
    {
        await Postgres.ResetAsync();
        App = await InvestigateApp.StartAsync(Postgres.ConnectionString, Configure);
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
