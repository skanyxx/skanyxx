namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class TicketsTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;
    protected FakeKAgent KAgent { get; private set; } = null!;
    protected TicketsApp App { get; set; } = null!;

    public async Task InitializeAsync()
    {
        await Postgres.ResetAsync();
        KAgent = await FakeKAgent.StartAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await KAgent.DisposeAsync();
    }
}
