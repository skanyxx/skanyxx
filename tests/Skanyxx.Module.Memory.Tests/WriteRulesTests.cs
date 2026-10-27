using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>memory-writes.md §4 and memory-store.md: dedupe, per-card coalescing, history, quotas, review scopes.</summary>
public sealed class WriteRulesTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task AnIdenticalUpdate_IsNotAWrite_ADifferentOneIsVersioned_AndTheOldVersionKept()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var same = await ana.PutCardAsync("personal:ana", "refund-window", version: 1);
        var changed = await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "We refund within 30 days");

        Assert.Equal(1, (await same.CardAsync()).Version);
        Assert.Equal(2, (await changed.CardAsync()).Version);
        var history = await ana.GetFromJsonAsync<List<CardVersionDto>>("/api/memory/cards/personal:ana/refund-window/history",
            CardApi.Json, TestContext.Current.CancellationToken);
        Assert.Equal("We refund within 14 days", Assert.Single(history!).What);
    }

    [Fact]
    public async Task History_KeepsTheWholeReplacedCard()
    {
        // C-5: body, source, links, keywords, supersession and attribution of a replaced version survive the write.
        var ana = App.Client("ana");
        (await ana.PutAsJsonAsync("/api/memory/cards/personal:ana/refund-window", new
        {
            version = 0, type = "decision", what = "We refund within 14 days", why = "Policy X", body = "old body", source = "https://wiki.example/old",
            tickets = new[] { "SDB-1" }, links = new[] { "company/policy" }, keywords = new[] { "refunds" }
        }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await ana.PutAsJsonAsync("/api/memory/cards/personal:ana/refund-window", new
        {
            version = 1, type = "decision", what = "We refund within 30 days", why = "Policy Y", body = "new body", source = "https://wiki.example/new",
            tickets = new[] { "SDB-2" }, links = new[] { "company/other" }, keywords = new[] { "returns" }
        }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var history = await ana.GetFromJsonAsync<List<CardVersionDto>>("/api/memory/cards/personal:ana/refund-window/history",
            CardApi.Json, TestContext.Current.CancellationToken);

        var old = Assert.Single(history!);
        Assert.Equal(1, old.Version);
        Assert.Equal("old body", old.Body);
        Assert.Equal("https://wiki.example/old", old.Source);
        Assert.Equal(["SDB-1"], old.Tickets);
        Assert.Equal(["company/policy"], old.Links);
        Assert.Equal(["refunds"], old.Keywords);
        Assert.Equal("manual", old.Origin);
        Assert.Null(old.SupersededBy);
    }

    [Fact]
    public async Task OneCallerWritingOneCard_IsCoalesced_OtherCardsStillWrite()
    {
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString,
            extra: new Dictionary<string, string?> { ["Memory:UpsertsPerKeyPerMinute"] = "2" });
        var ana = app.Client("ana");
        await ana.PutCardAsync("personal:ana", "busy");
        await ana.PutCardAsync("personal:ana", "busy", version: 1, what: "two");

        var third = await ana.PutCardAsync("personal:ana", "busy", version: 2, what: "three");
        var other = await ana.PutCardAsync("personal:ana", "other");

        Assert.Equal((HttpStatusCode)429, third.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task AFullScope_RefusesNewCards()
    {
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString,
            extra: new Dictionary<string, string?> { ["Memory:MaxCardsPerPersonalScope"] = "1" });
        await app.Client("ana").PutCardAsync("personal:ana", "one");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client("ana").PutCardAsync("personal:ana", "two")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.Client("bob").PutCardAsync("personal:bob", "two")).StatusCode);
    }

    [Fact]
    public async Task TicketsAndLinks_AreValidated()
    {
        var ana = App.Client("ana");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PutLinkedCardAsync("personal:ana", "a", "w", "y", tickets: ["not a ticket"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PutLinkedCardAsync("personal:ana", "a", "w", "y", links: ["javascript:alert(1)"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PutLinkedCardAsync("personal:ana", "a", "w", "y", tickets: [.. Enumerable.Range(1, 21).Select(i => $"SDB-{i}")])).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await ana.PutLinkedCardAsync("personal:ana", "a", "w", "y", tickets: ["SDB-58"], links: ["company/refund-window", "chat:abc", "https://x.example/a"])).StatusCode);
    }

    [Fact]
    public async Task InAReviewScope_AnAgentsWriteIsACandidate_UntilASupervisorPublishesIt()
    {
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString,
            extra: new Dictionary<string, string?> { ["Memory:ReviewScopes:0"] = "company" });
        await app.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await using var mcp = await app.McpAsync("writer");
        await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "refund-window", ["type"] = "decision", ["what"] = "We refund within 14 days", ["why"] = "Policy X", ["scope"] = "company"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var before = await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" },
            cancellationToken: TestContext.Current.CancellationToken);
        var employee = await app.Client("ana").PostAsJsonAsync("/api/memory/cards/company/refund-window/status",
            new { version = 1, status = "published" }, TestContext.Current.CancellationToken);
        var published = await app.SupervisorClient().PostAsJsonAsync("/api/memory/cards/company/refund-window/status",
            new { version = 1, status = "published" }, TestContext.Current.CancellationToken);
        var after = await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("refund-window", Text(before));
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Contains("refund-window", Text(after));
    }

    [Fact]
    public async Task Supersede_MarksStale_PointsAtTheReplacement_AndLeavesSearch()
    {
        var boss = App.SupervisorClient();
        await boss.PutCardAsync("company", "refund-14", what: "We refund within 14 days");
        await boss.PutCardAsync("company", "refund-30", what: "We refund within 30 days");

        var stale = await boss.PostAsJsonAsync("/api/memory/cards/company/refund-14/status",
            new { version = 1, status = "stale", supersededBy = "company/refund-30" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        var card = await stale.CardAsync();
        Assert.Equal("stale", card.Status);
        Assert.Equal("company/refund-30", card.SupersededBy);
        Assert.Equal(["refund-30"], (await App.Client("ana").SearchAsync("refund")).Select(h => h.Key));
    }

    [Fact]
    public async Task Supersede_ByACardTheWriterCannotOpen_IsNotFound()
    {
        await App.Client("bob").PutCardAsync("personal:bob", "secret");
        await App.Client("ana").PutCardAsync("personal:ana", "mine");

        var response = await App.Client("ana").PostAsJsonAsync("/api/memory/cards/personal:ana/mine/status",
            new { version = 1, status = "stale", supersededBy = "personal:bob/secret" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Status_AndHistory_FollowTheScopesRules()
    {
        await App.Client("ana").PutCardAsync("personal:ana", "mine");

        var write = await App.Client("bob").PostAsJsonAsync("/api/memory/cards/personal:ana/mine/status",
            new { version = 1, status = "stale" }, TestContext.Current.CancellationToken);
        var read = await App.Client("bob").GetAsync("/api/memory/cards/personal:ana/mine/history", TestContext.Current.CancellationToken);
        var anonymous = await App.Client().GetAsync("/api/memory/cards/personal:ana/mine/history", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task McpUpsert_CarriesTicketsAndLinks()
    {
        await using var mcp = await App.McpAsync("seed", userId: Users.Ana);
        await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "eu-form", ["type"] = "fact", ["what"] = "EU form needs IBAN", ["why"] = "Seen on SDB-58",
            ["tickets"] = new[] { "SDB-58" }, ["links"] = new[] { "company/refund-window" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var db = Postgres.CreateDbContext();
        var card = await db.Cards.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["SDB-58"], card.Tickets);
        Assert.Equal(["company/refund-window"], card.Links);
        Assert.Equal("seed", card.AgentId);
        Assert.Equal(CardOrigin.Manual, card.Origin);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? System.Text.Json.JsonSerializer.Serialize(s) : "");
}
