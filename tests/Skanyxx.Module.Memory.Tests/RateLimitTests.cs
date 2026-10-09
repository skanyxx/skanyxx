using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class RateLimitTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    protected override int UpsertsPerMinute => 3;
    protected override int UpsertsPerMinuteTotal => 6;

    [Fact]
    public async Task AgentOverLimit_Rejected_OtherAgentsUnaffected()
    {
        var boss = App.SupervisorClient();
        await boss.SetGrantsAsync("loop", new { scope = "company", canSearch = true, canUpsert = true });
        await boss.SetGrantsAsync("calm", new { scope = "company", canSearch = true, canUpsert = true });
        await using var loop = await App.McpAsync("loop");
        await using var calm = await App.McpAsync("calm");

        for (var i = 0; i < 3; i++)
            Assert.NotEqual(true, (await loop.CallToolAsync("memory_upsert", Args($"loop-{i}"), cancellationToken: TestContext.Current.CancellationToken)).IsError);
        var overLimit = await loop.CallToolAsync("memory_upsert", Args("loop-3"), cancellationToken: TestContext.Current.CancellationToken);
        var other = await calm.CallToolAsync("memory_upsert", Args("calm-0"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(overLimit.IsError);
        Assert.Contains("rate limit", string.Concat(overLimit.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
        Assert.NotEqual(true, other.IsError);
        Assert.Equal(4, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task Lift_CountsAgainstTheWriteLimit()
    {
        var ana = App.Client("ana");
        for (var i = 0; i < 3; i++)
            App.Org.Join("ana", $"t{i}", "ops");
        Assert.Equal(HttpStatusCode.Created, (await ana.PutCardAsync("personal:ana", "refund-window")).StatusCode);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Created, (await ana.LiftAsync("personal:ana", "refund-window", $"team:t{i}")).StatusCode);

        var overLimit = await ana.LiftAsync("personal:ana", "refund-window", "team:t2");

        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
        Assert.Equal(3, await Postgres.CardCountAsync());
    }

    private static Dictionary<string, object?> Args(string key) => new()
    {
        ["key"] = key, ["type"] = "fact", ["what"] = "w", ["why"] = "y", ["scope"] = "company"
    };

    [Fact]
    public async Task OverLimit_Rejected_OtherCallersUnaffected()
    {
        var ana = App.Client("ana");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await ana.PutCardAsync("personal:ana", $"card-{i}")).StatusCode);

        var overLimit = await ana.PutCardAsync("personal:ana", "card-3");
        var bob = await App.Client("bob").PutCardAsync("personal:bob", "card-0");

        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bob.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(4, db.Cards.Count());
    }

    // CR M1: the caller id is a spoofable header, so a fresh id per request must still hit a shared limit.
    [Fact]
    public async Task ManyCallerIds_ShareOneTotalLimit()
    {
        var statuses = new List<HttpStatusCode>();
        foreach (var user in new[] { "u1", "u2", "u3", "u4", "u5", "u6", "u7" })
            statuses.Add((await App.Client(user).PutCardAsync($"personal:{user}", "card")).StatusCode);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Created, 6).Append(HttpStatusCode.TooManyRequests), statuses);
        Assert.Equal(6, await Postgres.CardCountAsync());
    }

    // A caller refused by its own limit does not spend the shared budget.
    [Fact]
    public async Task ACallerOverItsOwnLimit_DoesNotSpendTheTotal()
    {
        var ana = App.Client("ana");
        for (var i = 0; i < 6; i++)
            await ana.PutCardAsync("personal:ana", $"card-{i}");

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await App.Client("bob").PutCardAsync("personal:bob", $"card-{i}")).StatusCode);
    }

    /// <summary>
    /// A rename takes a write slot only when it is about to be written: refusals (missing card, stale version, taken key)
    /// are reads, and a re-submitted stale form must not spend the person's writes.
    /// </summary>
    [Fact]
    public async Task Rename_SpendsASlotOnlyOnAWrite()
    {
        var ana = App.Client("ana");
        Assert.Equal(HttpStatusCode.Created, (await ana.PutCardAsync("personal:ana", "refund-window")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ana.PutCardAsync("personal:ana", "refunds")).StatusCode);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await RenameAsync(ana, "no-such-card", "anything", 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await RenameAsync(ana, "refund-window", "refund-period", 7)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await RenameAsync(ana, "refund-window", "refunds", 1)).StatusCode);
        }
        var renamed = await RenameAsync(ana, "refund-window", "refund-period", 1);
        var overLimit = await RenameAsync(ana, "refund-period", "refund-days", 2);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, string key, string newKey, int version) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"/api/memory/cards/personal:ana/{key}/rename", new { newKey, version });
}
