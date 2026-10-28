using System.Net.Http.Json;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Sessions;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// requirements.md O1/§3.4: a new chat's first message carries related cards — only ones the person may read AND the
/// agent may search, published only, within the token budget. Another person's private card never reaches a prompt.
/// </summary>
public sealed class RelatedMemoryTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join("ana", "billing", "finance");
        await App.Client("ana").PutCardAsync("personal:ana", "ana-refund", what: "Ana refunds by hand");
        await App.Client("bob").PutCardAsync("personal:bob", "bob-refund", what: "Bob refund secret");
        await App.SupervisorClient().PutCardAsync("company", "company-refund", what: "Company refunds within 14 days");
        await App.Client("ana").PutCardAsync("team:billing", "team-refund", what: "Billing team refund queue");
    }

    private static string[] Keys(Core.Platform.Memory.MemoryContext context) => [.. context.Cards.Select(c => $"{c.Scope}/{c.Key}").Order()];

    [Fact]
    public async Task DefaultAgent_GetsCompanyAndTheirOwnPersonal_NeverAnotherPersonsOrATeamWithoutGrant()
    {
        var ana = await App.RelatedAsync("ana", "seed", "how do refunds work");
        var bob = await App.RelatedAsync("bob", "seed", "how do refunds work");

        Assert.Equal(["company/company-refund", "personal:ana/ana-refund"], Keys(ana));
        Assert.Equal(["company/company-refund", "personal:bob/bob-refund"], Keys(bob));
        Assert.DoesNotContain("Bob refund secret", ana.Block);
        Assert.Contains("Ana refunds by hand", ana.Block);
    }

    [Fact]
    public async Task ATeamGrant_ReachesOnlyMembers_AndOnlyThroughTheGrantedAgent()
    {
        await App.OwnerClient().SetGrantsAsync("billing-bot",
            new { scope = "team:billing", canSearch = true }, new { scope = "company", canSearch = true });

        Assert.Contains("team:billing/team-refund", Keys(await App.RelatedAsync("ana", "billing-bot", "refund queue")));
        Assert.DoesNotContain("team:billing/team-refund", Keys(await App.RelatedAsync("bob", "billing-bot", "refund queue")));
        Assert.DoesNotContain("team:billing/team-refund", Keys(await App.RelatedAsync("ana", "seed", "refund queue")));
        // The grant has no personal: even Ana's own card stays out of this agent's prompt.
        Assert.DoesNotContain("personal:ana/ana-refund", Keys(await App.RelatedAsync("ana", "billing-bot", "refund")));
    }

    [Fact]
    public async Task ASupervisorsOversight_NeverReachesAPrompt()
    {
        await App.OwnerClient().SetGrantsAsync("billing-bot", new { scope = "team:billing", canSearch = true });

        // The supervisor reads team:billing in the library, but is not a member: the agent gets nothing from it.
        Assert.Empty((await App.RelatedAsync(MemoryApp.Supervisor, "billing-bot", "refund queue")).Cards);
    }

    [Fact]
    public async Task OnlyPublishedCards_AreInjected()
    {
        var ana = App.Client("ana");
        var card = await (await ana.GetAsync("/api/memory/cards/personal:ana/ana-refund", TestContext.Current.CancellationToken)).CardAsync();
        await ana.PostAsJsonAsync("/api/memory/cards/personal:ana/ana-refund/status", new { version = card.Version, status = "stale" },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("personal:ana/ana-refund", Keys(await App.RelatedAsync("ana", "seed", "refunds")));
    }

    [Fact]
    public async Task ATicketMatch_RanksFirst()
    {
        await App.Client("ana").PutLinkedCardAsync("personal:ana", "eu-form", "EU form needs IBAN", "Learnt on the run", tickets: ["SDB-58"]);

        var context = await App.RelatedAsync("ana", "seed", "continue the work on SDB-58 refunds");

        Assert.Equal("eu-form", context.Cards[0].Key);
    }

    [Fact]
    public async Task NothingRelated_InjectsNothing_AndBadIdsAreRefusedQuietly()
    {
        Assert.Equal("", (await App.RelatedAsync("ana", "seed", "weather in tokyo")).Block);
        Assert.Empty((await App.RelatedAsync("Not Valid", "seed", "refunds")).Cards);
        Assert.Empty((await App.RelatedAsync("ana", "bad agent", "refunds")).Cards);
    }

    [Fact]
    public void TheBudget_IsHeld_WithWholeCardsOnly()
    {
        var hits = Enumerable.Range(0, 20)
            .Select(i => new CardHit("company", $"k{i}", 1, "fact", new string('w', 200), new string('y', 400), "x", DateTime.UtcNow, []))
            .ToList();

        var fitted = RelatedMemory.Fit(hits, maxTokens: 400);

        Assert.True(fitted.Tokens <= 400);
        Assert.InRange(fitted.Cards.Count, 1, 2);
        Assert.All(fitted.Cards, c => Assert.Contains(new string('w', 200), fitted.Block));
        Assert.EndsWith(RelatedMemory.Footer, fitted.Block);
        Assert.Same(Core.Platform.Memory.MemoryContext.None, RelatedMemory.Fit([], 400));
    }

    [Fact]
    public async Task Injection_CanBeTurnedOff()
    {
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString,
            extra: new Dictionary<string, string?> { ["Memory:Injection:Enabled"] = "false" });
        Assert.Empty((await app.RelatedAsync("ana", "seed", "refunds")).Cards);
    }
}
