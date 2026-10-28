using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>C-4, D307: a scope's quota bounds what it stores, whichever path adds the card and however many at once.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ScopeQuotaTests(PostgresFixture postgres) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(postgres.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<MemoryApp> StartAsync(string setting) =>
        MemoryApp.StartAsync(postgres.ConnectionString, extra: new Dictionary<string, string?> { [setting] = "1" });

    [Fact]
    public async Task ALift_IntoAFullScope_IsRefused()
    {
        await using var app = await StartAsync("Memory:MaxCardsPerScope");
        var boss = app.SupervisorClient();
        await boss.PutCardAsync("company", "already-there");
        await boss.PutCardAsync($"personal:{MemoryApp.Supervisor}", "to-lift");

        var lift = await boss.LiftAsync($"personal:{MemoryApp.Supervisor}", "to-lift", "company");

        Assert.Equal(HttpStatusCode.Forbidden, lift.StatusCode);
        await using var db = postgres.CreateDbContext();
        Assert.Single(db.Cards, c => c.Scope == "company");
    }

    [Fact]
    public async Task ConcurrentCreates_IntoTheLastFreeSlot_StoreOneCard()
    {
        await using var app = await StartAsync("Memory:MaxCardsPerPersonalScope");
        for (var round = 0; round < 8; round++)
        {
            await postgres.ResetAsync();
            var ana = app.Client("ana");

            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => ana.PutCardAsync("personal:ana", $"card-{round}-{i}")));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
            Assert.Equal(1, await postgres.CardCountAsync());
        }
    }
}
