using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class McpTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private static Dictionary<string, object?> Upsert(string key, string scope = "personal", int version = 0) => new()
    {
        ["key"] = key, ["type"] = "decision", ["what"] = "We refund within 14 days",
        ["why"] = "Finance policy X", ["version"] = version, ["scope"] = scope
    };

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");

    [Fact]
    public async Task ToolsList_IsExactlySearchAndUpsert()
    {
        await using var client = await App.McpAsync("seed");

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["memory_search", "memory_upsert"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task Search_NeverReturnsBodyOrSource()
    {
        await App.SupervisorClient()
            .PutCardAsync("company", "refund-window", body: "SECRET-BODY-TEXT", source: "s3://secret-bucket/file.pdf");
        await using var client = await App.McpAsync("seed");

        var result = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var text = Text(result);
        Assert.Contains("refund-window", text);
        Assert.DoesNotContain("SECRET-BODY-TEXT", text);
        Assert.DoesNotContain("s3://secret-bucket", text);
        Assert.DoesNotContain("\"body\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"source\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefaultAgent_SearchesCompanyAndCallersPersonal()
    {
        await App.SupervisorClient().PutCardAsync("company", "company-refund");
        await App.Client(Users.Bob).PutCardAsync($"personal:{Users.Bob}", "bob-refund");
        await App.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "ana-refund");
        App.Org.Join(Users.Bob, "billing", "finance");
        await App.Client(Users.Bob).PutCardAsync("team:billing", "team-refund");
        await using var client = await App.McpAsync("seed", userId: Users.Bob);

        var result = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken);

        var text = Text(result);
        Assert.Contains("company-refund", text);
        Assert.Contains("bob-refund", text);
        Assert.DoesNotContain("ana-refund", text);
        Assert.DoesNotContain("team-refund", text);
    }

    [Fact]
    public async Task DefaultAgent_UpsertsCallersPersonalOnly()
    {
        await using var client = await App.McpAsync("seed", userId: Users.Ana);

        var personal = await client.CallToolAsync("memory_upsert", Upsert("refund-window"), cancellationToken: TestContext.Current.CancellationToken);
        var company = await client.CallToolAsync("memory_upsert", Upsert("refund-window", scope: "company"), cancellationToken: TestContext.Current.CancellationToken);
        var team = await client.CallToolAsync("memory_upsert", Upsert("refund-window", scope: "team:billing"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, personal.IsError);
        Assert.Contains($"personal:{Users.Ana}", Text(personal));
        Assert.True(company.IsError);
        Assert.Contains("No upsert grant", Text(company));
        Assert.True(team.IsError);
        Assert.Equal(1, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task DefaultAgent_WithoutUser_CannotUpsertPersonal()
    {
        await using var client = await App.McpAsync("seed");

        var result = await client.CallToolAsync("memory_upsert", Upsert("refund-window"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task ExplicitGrants_ReplaceTheDefault()
    {
        await App.SupervisorClient().SetGrantsAsync("seed", new { scope = "company", canSearch = true, canUpsert = false });
        await using var client = await App.McpAsync("seed", userId: Users.Ana);

        var result = await client.CallToolAsync("memory_upsert", Upsert("refund-window"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("No upsert grant", Text(result));
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task SearchGrant_DoesNotImplyUpsert()
    {
        await App.SupervisorClient().SetGrantsAsync("faq", new { scope = "company", canSearch = true, canUpsert = false });
        await using var client = await App.McpAsync("faq");

        var result = await client.CallToolAsync("memory_upsert", Upsert("refund-window", scope: "company"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("No upsert grant", Text(result));
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task PersonalUpsertGrant_WritesTheCallersPersonalScope()
    {
        await App.SupervisorClient().SetGrantsAsync("seed",
            new { scope = "personal", canSearch = true, canUpsert = true },
            new { scope = "company", canSearch = true, canUpsert = false });
        await using var client = await App.McpAsync("seed", userId: Users.Ana);

        var created = await client.CallToolAsync("memory_upsert", Upsert("refund-window"), cancellationToken: TestContext.Current.CancellationToken);
        var found = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, created.IsError);
        Assert.Contains($"personal:{Users.Ana}", Text(created));
        Assert.Contains($"{Users.Ana} via seed", Text(created));
        Assert.Contains("refund-window", Text(found));
    }

    [Fact]
    public async Task PersonalUpsert_WithoutUserContext_IsAnError()
    {
        await App.SupervisorClient().SetGrantsAsync("seed", new { scope = "personal", canSearch = true, canUpsert = true });
        await using var client = await App.McpAsync("seed");

        var result = await client.CallToolAsync("memory_upsert", Upsert("refund-window"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("No user context", Text(result));
    }

    [Fact]
    public async Task StaleUpsert_IsAConflictError_AndCardUnchanged()
    {
        await App.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await using var client = await App.McpAsync("writer");
        await client.CallToolAsync("memory_upsert", Upsert("refund-window", scope: "company"), cancellationToken: TestContext.Current.CancellationToken);

        var stale = await client.CallToolAsync("memory_upsert", Upsert("refund-window", scope: "company"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stale.IsError);
        Assert.Contains("Conflict", Text(stale));
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(1, Assert.Single(db.Cards).Version);
    }

    [Fact]
    public async Task InvalidArguments_ReportedAsToolError()
    {
        await App.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await using var client = await App.McpAsync("writer");
        var args = Upsert("Bad_Key", scope: "company");
        args["what"] = new string('w', 201);

        var result = await client.CallToolAsync("memory_upsert", args, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError, Text(result));
        Assert.Contains("key:", Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("what:", Text(result), StringComparison.OrdinalIgnoreCase);
    }
}
