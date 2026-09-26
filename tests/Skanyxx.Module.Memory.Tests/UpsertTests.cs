using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class UpsertTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task Create_ReturnsCreatedAtVersionOne()
    {
        var response = await App.Client("ana").PutCardAsync("personal:ana", "refund-window");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = await response.CardAsync();
        Assert.Equal(1, card.Version);
        Assert.Equal("decision", card.Type);
        Assert.Equal("ana", card.Who);
        Assert.Equal("published", card.Status);
    }

    [Fact]
    public async Task CreateTwice_ConflictsWithCurrentCard()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var second = await ana.PutCardAsync("personal:ana", "refund-window", what: "Something else");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.JsonAsync();
        Assert.Equal(1, body.GetProperty("current").GetProperty("version").GetInt32());
        Assert.Equal("We refund within 14 days", body.GetProperty("current").GetProperty("what").GetString());
    }

    [Fact]
    public async Task Update_WithCurrentVersion_BumpsVersion()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var updated = await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "We refund within 30 days");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var card = await updated.CardAsync();
        Assert.Equal(2, card.Version);
        Assert.Equal("We refund within 30 days", card.What);
    }

    [Fact]
    public async Task Update_StaleVersion_Returns409WithCurrent()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "v2");

        var stale = await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "lost update");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await stale.JsonAsync()).GetProperty("current");
        Assert.Equal(2, current.GetProperty("version").GetInt32());
        Assert.Equal("v2", current.GetProperty("what").GetString());
    }

    [Fact]
    public async Task Update_MissingCard_Returns404()
    {
        var response = await App.Client("ana").PutCardAsync("personal:ana", "nothing-here", version: 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentUpdates_SameVersion_ExactlyOneWins()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            App.Client("ana").PutCardAsync("personal:ana", "refund-window", version: 1, what: $"writer {i}")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = Postgres.CreateDbContext();
        var card = Assert.Single(db.Cards);
        Assert.Equal(2, card.Version);
    }

    [Fact]
    public async Task ConcurrentCreates_ExactlyOneWins()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            App.Client("ana").PutCardAsync("personal:ana", "refund-window", what: $"writer {i}")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Upsert_OtherUsersPersonalScope_Forbidden()
    {
        var response = await App.Client("ana").PutCardAsync("personal:bob", "refund-window");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_Company_OnlySupervisor()
    {
        var byEmployee = await App.Client("ana").PutCardAsync("company", "refund-window");
        var bySupervisor = await App.Client(MemoryApp.Supervisor).PutCardAsync("company", "refund-window");

        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bySupervisor.StatusCode);
    }
}
