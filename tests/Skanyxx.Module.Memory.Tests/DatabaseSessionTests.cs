using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class DatabaseSessionTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    // SEC M5: memory has its own bounded pool, so a flood here cannot starve tickets (Npgsql keys pools by string).
    [Fact]
    public async Task TheModule_ConnectsWithItsOwnApplicationName_AndABoundedPool()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();

        var name = await db.Database.SqlQuery<string>($"SELECT current_setting('application_name') AS \"Value\"").SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        var settings = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());

        Assert.Equal("skanyxx-memory", name);
        Assert.Equal(40, settings.MaxPoolSize);
    }

    // Verifier: a duplicate create is an expected 409, and must not log at Error.
    [Fact]
    public async Task ACreateThatLosesToAnExistingCard_LogsNoError()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var stale = await ana.PutCardAsync("personal:ana", "refund-window");

        Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty(App.Errors);
    }

    // CR L3: only the expected conflict is quiet; a real SQL failure is not lowered below Error.
    [Fact]
    public async Task AFailingCommand_LogsAnError()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("SELECT * FROM no_such_table", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(App.Errors, e => e.Contains("no_such_table"));
    }
}
