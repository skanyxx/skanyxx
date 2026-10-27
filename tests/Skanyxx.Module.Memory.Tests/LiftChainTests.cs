using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>memory-hierarchy.md: personal → team → department → company, each step gated, links never pointing down.</summary>
public sealed class LiftChainTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task ASessionCard_LiftsUpTheChain_WithItsTickets_AndOnlyASupervisorTakesItToCompany()
    {
        App.Org.Join("ana", "billing", "finance");
        await App.ChatTurnAsync(new ChatTurnDigest("ana", "seed", "kagent/seed", "s-9", "How do refunds work for SDB-58?", "Within 14 days.", true));
        var ana = App.Client("ana");

        var team = await ana.LiftAsync("personal:ana", "chat-s-9", "team:billing");
        var department = await ana.LiftAsync("team:billing", "chat-s-9", "department:finance");
        var employeeToCompany = await ana.LiftAsync("department:finance", "chat-s-9", "company");
        var down = await ana.LiftAsync("department:finance", "chat-s-9", "team:billing");
        var company = await App.SupervisorClient().LiftAsync("department:finance", "chat-s-9", "company");

        Assert.Equal(HttpStatusCode.Created, team.StatusCode);
        Assert.Equal(HttpStatusCode.Created, department.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeToCompany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, down.StatusCode);
        Assert.Equal(HttpStatusCode.Created, company.StatusCode);
        var copy = await company.CardAsync();
        Assert.Equal(["SDB-58"], copy.Tickets);
        Assert.Equal("chat", copy.Origin);
        // Everyone now finds it, by ticket; the transcript was never stored, only the pointer.
        Assert.Contains("chat-s-9", (await App.Client("bob").SearchAsync("SDB-58")).Select(h => h.Key));
        Assert.Equal("chat:s-9", copy.Source);
    }

    [Fact]
    public async Task ALift_DropsLinksIntoLowerScopes()
    {
        App.Org.Join("ana", "billing", "finance");
        var ana = App.Client("ana");
        await ana.PutLinkedCardAsync("personal:ana", "card", "w", "y",
            links: ["personal:ana/private-note", "company/policy", "chat:s-1", "https://x.example/a"]);

        var copy = await (await ana.LiftAsync("personal:ana", "card", "team:billing")).CardAsync();

        Assert.Equal(["company/policy", "chat:s-1", "https://x.example/a"], copy.Links);
    }

    [Fact]
    public async Task LibraryFilters_NarrowWithinReadableScopesOnly()
    {
        await App.Client("ana").PutLinkedCardAsync("personal:ana", "ana-58", "w", "y", tickets: ["SDB-58"]);
        await App.Client("bob").PutLinkedCardAsync("personal:bob", "bob-58", "w", "y", tickets: ["SDB-58"]);
        await App.SupervisorClient().PutLinkedCardAsync("company", "company-58", "w", "y", tickets: ["SDB-58"]);
        await App.SupervisorClient().PutLinkedCardAsync("company", "company-other", "w", "y");

        var shelf = await App.Client("ana").GetFromJsonAsync<LibraryShelf>("/api/memory/library?ticket=SDB-58", CardApi.Json,
            TestContext.Current.CancellationToken);
        var bad = await App.Client("ana").GetAsync("/api/memory/library?ticket=nope", TestContext.Current.CancellationToken);
        var type = await App.Client("ana").GetFromJsonAsync<LibraryShelf>("/api/memory/library?type=fact", CardApi.Json,
            TestContext.Current.CancellationToken);

        Assert.Equal(["ana-58", "company-58"], shelf!.Cards.Select(c => c.Key).Order());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(type!.Cards);
    }
}
