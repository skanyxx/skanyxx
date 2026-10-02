namespace Skanyxx.Module.Memory.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class MemoryTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;
    protected MemoryApp App { get; private set; } = null!;

    protected virtual int UpsertsPerMinute => 1000;
    protected virtual int UpsertsPerMinuteTotal => 10_000;

    public virtual async Task InitializeAsync()
    {
        await Postgres.ResetAsync();
        App = await MemoryApp.StartAsync(Postgres.ConnectionString, UpsertsPerMinute, UpsertsPerMinuteTotal);
    }

    public async Task DisposeAsync() => await App.DisposeAsync();
}
