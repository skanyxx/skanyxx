using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class BodyPreservationTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task RestUpdate_WithoutBodyOrSource_KeepsThem()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window", body: "details", source: "chat://42");

        var updated = await (await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "v2")).CardAsync();

        Assert.Equal("details", updated.Body);
        Assert.Equal("chat://42", updated.Source);
    }

    [Fact]
    public async Task RestUpdate_WithNewBody_ReplacesIt()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window", body: "details");

        var updated = await (await ana.PutCardAsync("personal:ana", "refund-window", version: 1, body: "new details")).CardAsync();

        Assert.Equal("new details", updated.Body);
    }

    [Fact]
    public async Task AgentUpdate_KeepsTheLibraryOnlyFields()
    {
        var boss = App.Client(MemoryApp.Supervisor);
        await boss.SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await boss.PutCardAsync("company", "refund-window", body: "long explanation", source: "s3://docs/policy.pdf");
        await using var client = await App.McpAsync("writer");

        var result = await client.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "refund-window", ["type"] = "decision", ["what"] = "We refund within 30 days",
            ["why"] = "Policy changed", ["version"] = 1, ["scope"] = "company"
        });

        Assert.NotEqual(true, result.IsError);
        var card = await (await boss.GetAsync("/api/memory/cards/company/refund-window")).CardAsync();
        Assert.Equal(2, card.Version);
        Assert.Equal("long explanation", card.Body);
        Assert.Equal("s3://docs/policy.pdf", card.Source);
    }
}
