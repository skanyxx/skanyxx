using System.Net;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Tests.Infrastructure;
using Testcontainers.Redis;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// memory-stack.md / D068: Dragonfly (Redis protocol; Redis here) as a search cache and shared write limits only. A cached
/// search never crosses access, a write ends it, and without Dragonfly everything works as before.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DragonflyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        await _redis.StartAsync();
    }

    public ValueTask DisposeAsync() => _redis.DisposeAsync();

    private Task<MemoryApp> StartAsync(string configuration, int perCaller = 1000) =>
        MemoryApp.StartAsync(postgres.ConnectionString, upsertsPerMinute: perCaller,
            extra: new Dictionary<string, string?> { ["Memory:Dragonfly:Configuration"] = configuration });

    [Fact]
    public async Task ASearch_IsCached_UntilAWriteToItsScope()
    {
        await using var app = await StartAsync(_redis.GetConnectionString());
        var boss = app.SupervisorClient();
        await boss.PutCardAsync("company", "refund-window");
        Assert.Single(await app.Client("ana").SearchAsync("refund"));

        // Behind memory's back: the cache still answers.
        await using (var db = postgres.CreateDbContext())
            await db.Cards.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        Assert.Single(await app.Client("ana").SearchAsync("refund"));

        // Through memory: the write ends every cached search over company.
        await boss.PutCardAsync("company", "refund-other", what: "Refunds of other kinds");
        Assert.Equal(["refund-other"], (await app.Client("ana").SearchAsync("refund")).Select(h => h.Key));
    }

    [Fact]
    public async Task ACachedSearch_NeverCrossesAccess()
    {
        await using var app = await StartAsync(_redis.GetConnectionString());
        await app.Client("ana").PutCardAsync("personal:ana", "ana-refund");

        Assert.Single(await app.Client("ana").SearchAsync("refund"));
        Assert.Empty(await app.Client("bob").SearchAsync("refund"));
        Assert.Empty((await app.RelatedAsync("bob", "seed", "refund")).Cards);
    }

    [Fact]
    public async Task WriteLimits_AreSharedByEveryReplica()
    {
        await using var first = await StartAsync(_redis.GetConnectionString(), perCaller: 2);
        await using var second = await StartAsync(_redis.GetConnectionString(), perCaller: 2);

        Assert.Equal(HttpStatusCode.Created, (await first.Client("ana").PutCardAsync("personal:ana", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.Client("ana").PutCardAsync("personal:ana", "b")).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await first.Client("ana").PutCardAsync("personal:ana", "c")).StatusCode);
    }

    [Fact]
    public async Task AnUnreachableDragonfly_FallsBackToWhatMemoryDoesWithoutIt()
    {
        await using var app = await StartAsync("127.0.0.1:1", perCaller: 2);

        Assert.Equal(HttpStatusCode.Created, (await app.Client("ana").PutCardAsync("personal:ana", "a")).StatusCode);
        Assert.Single(await app.Client("ana").SearchAsync("refund"));
        Assert.Equal(HttpStatusCode.Created, (await app.Client("ana").PutCardAsync("personal:ana", "b")).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await app.Client("ana").PutCardAsync("personal:ana", "c")).StatusCode);
        Assert.Empty(app.Errors);
    }

    [Fact]
    public async Task AnInvalidConfiguration_StopsStartup()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(",,,"));
    }
}
