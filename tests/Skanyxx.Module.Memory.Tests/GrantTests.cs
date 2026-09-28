using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Memory.Features.Grants;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class GrantTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task OnlySupervisor_SetsGrants()
    {
        var byEmployee = await App.Client("ana").SetGrantsAsync("seed", new { scope = "company", canSearch = true, canUpsert = true });

        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
    }

    [Fact]
    public async Task SetGrants_ReplacesPreviousGrants()
    {
        var boss = App.SupervisorClient();
        await boss.SetGrantsAsync("seed", new { scope = "company", canSearch = true, canUpsert = true });
        await boss.SetGrantsAsync("seed", new { scope = "team:billing", canSearch = true, canUpsert = false });

        var grants = await boss.GetFromJsonAsync<List<GrantEntry>>("/api/memory/grants/seed", CardApi.Json);

        Assert.Equal([new GrantEntry("team:billing", true, false)], grants);
    }

    [Fact]
    public async Task OnlySupervisor_ReadsGrants()
    {
        var response = await App.Client("ana").GetAsync("/api/memory/grants/seed");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoreThan50Grants_Rejected()
    {
        var grants = Enumerable.Range(0, 51).Select(i => (object)new { scope = $"team:t{i}", canSearch = true, canUpsert = false }).ToArray();

        var response = await App.SupervisorClient().SetGrantsAsync("seed", grants);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentGrantWrites_LeaveExactlyOneWritersSet()
    {
        var sets = Enumerable.Range(0, 10)
            .Select(i => new[] { $"team:t{i}", i % 2 == 0 ? "company" : $"department:d{i}" })
            .ToList();

        var responses = await Task.WhenAll(sets.Select(set => App.SupervisorClient().SetGrantsAsync("seed",
            set.Select(s => (object)new { scope = s, canSearch = true, canUpsert = false }).ToArray())));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var final = (await App.SupervisorClient().GetFromJsonAsync<List<GrantEntry>>("/api/memory/grants/seed", CardApi.Json))!
            .Select(g => g.Scope).Order().ToList();
        Assert.Contains(sets, set => set.Order().SequenceEqual(final));
    }

    [Fact]
    public async Task DenyGrant_RevokesTheAgent()
    {
        await App.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "refund-window");
        var set = await App.SupervisorClient().SetGrantsAsync("seed", new { scope = "company", canSearch = false, canUpsert = false });
        await using var client = await App.McpAsync("seed", userId: Users.Ana);

        var upsert = await client.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "other", ["type"] = "fact", ["what"] = "w", ["why"] = "y", ["version"] = 0, ["scope"] = "personal"
        });
        var search = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.True(upsert.IsError);
        Assert.DoesNotContain("refund-window", string.Concat(search.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
        Assert.Equal(1, await Postgres.CardCountAsync());
    }

    [Theory]
    [InlineData("{\"grants\":[]}")]
    [InlineData("{}")]
    public async Task EmptyGrantList_Rejected_BecauseItWouldMeanTheDefault(string json)
    {
        var response = await App.SupervisorClient().PutAsync("/api/memory/grants/seed",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"grants\":null}")]
    [InlineData("{\"grants\":[null]}")]
    public async Task NullGrants_AreAValidationError_NotA500(string json)
    {
        var response = await App.SupervisorClient().PutAsync("/api/memory/grants/seed",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TrailingNewline_InGrantScopeOrAgent_Rejected()
    {
        var boss = App.SupervisorClient();

        var badScope = await boss.SetGrantsAsync("seed", new { scope = "team:t\n", canSearch = true, canUpsert = false });
        var badAgent = await boss.SetGrantsAsync(Uri.EscapeDataString("seed\n"), new { scope = "company", canSearch = true, canUpsert = false });

        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badAgent.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Empty(db.Grants);
    }

    [Theory]
    [InlineData("personal:ana", true, true)]
    [InlineData("nowhere", true, false)]
    public async Task InvalidGrant_Rejected(string scope, bool canSearch, bool canUpsert)
    {
        var response = await App.SupervisorClient().SetGrantsAsync("seed", new { scope, canSearch, canUpsert });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
