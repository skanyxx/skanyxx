namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class ChatContextTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected ChatContextApp App { get; private set; } = null!;

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        App = await ChatContextApp.StartAsync(postgres.ConnectionString, Settings);
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
