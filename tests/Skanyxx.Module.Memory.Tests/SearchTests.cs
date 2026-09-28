using System.Net;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class SearchTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task Search_ReturnsPublishedOnly_Top5_BestMatchFirst()
    {
        var boss = App.SupervisorClient();
        await boss.PutCardAsync("company", "refund-window", what: "Refund window is 14 days", why: "Finance policy");
        for (var i = 0; i < 6; i++)
            await boss.PutCardAsync("company", $"weak-{i}", what: $"Unrelated item {i}", why: "Mentions a refund once");
        await boss.PutCardAsync("company", "old-refund-window", what: "Refund window is 7 days", why: "Old refund policy");
        await using (var db = Postgres.CreateDbContext())
            await db.Cards.Where(c => c.Key == "old-refund-window")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CardStatus.Stale));

        var hits = await App.Client("ana").SearchAsync("refund window");

        Assert.Equal(5, hits.Count);
        Assert.Equal("refund-window", hits[0].Key);
        Assert.DoesNotContain(hits, h => h.Key == "old-refund-window");
    }

    [Fact]
    public async Task Search_NaturalLanguageQuestion_Matches()
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window");

        var hits = await App.Client("ana").SearchAsync("How long do customers have to ask for a refund?");

        Assert.Equal("refund-window", Assert.Single(hits).Key);
    }

    [Theory]
    [InlineData("refund-window")]
    [InlineData("refund window")]
    [InlineData("windows")]
    public async Task Search_FindsACardByItsOwnKey_AsSlugOrWords(string query)
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window", what: "Fourteen days", why: "Policy");

        var hits = await App.Client("ana").SearchAsync(query);

        Assert.Equal("refund-window", Assert.Single(hits).Key);
    }

    [Fact]
    public async Task Search_HyphenatedWord_MatchesTheSpacedForm()
    {
        await App.SupervisorClient().PutCardAsync("company", "customer-calls", what: "Always follow up within a day", why: "Retention");

        var hits = await App.Client("ana").SearchAsync("follow-up");

        Assert.Equal("customer-calls", Assert.Single(hits).Key);
    }

    [Fact]
    public void Terms_CountWordsAfterPunctuation_AndDropOverlongWords()
    {
        var chain = string.Join('-', Enumerable.Range(0, 120).Select(i => $"w{i}"));
        var packed = string.Join(',', Enumerable.Range(0, 120).Select(i => $"p{i}"));
        var overlong = new string('x', Skanyxx.Module.Memory.Data.CardSearch.MaxWordLength + 1);

        var chained = Skanyxx.Module.Memory.Data.CardSearch.Terms($"follow-up {chain}");
        var commas = Skanyxx.Module.Memory.Data.CardSearch.Terms(packed);
        var longWord = Skanyxx.Module.Memory.Data.CardSearch.Terms($"refund {overlong}");

        Assert.Equal(Skanyxx.Module.Memory.Data.CardSearch.MaxTerms, chained.Length);
        Assert.Equal("w119", chained[0]);
        Assert.Equal(Skanyxx.Module.Memory.Data.CardSearch.MaxTerms, commas.Length);
        Assert.Equal(["refund"], longWord);
    }

    [Fact]
    public void Terms_AreCapped_KeepingTheLastWords_CaseInsensitively()
    {
        var words = Enumerable.Range(0, 100).Select(i => $"w{i}").Append("W99").Append("Refund");

        var terms = Skanyxx.Module.Memory.Data.CardSearch.Terms(string.Join(' ', words));

        Assert.Equal(Skanyxx.Module.Memory.Data.CardSearch.MaxTerms, terms.Length);
        Assert.Contains("Refund", terms);
        Assert.Single(terms, t => t.Equals("w99", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("w0", terms);
    }

    [Fact]
    public async Task Search_LongQuestion_UsesItsLastWordsToo()
    {
        await App.SupervisorClient().PutCardAsync("company", "warehouse-hours", what: "Closes at 16:00", why: "Carrier pickup");
        var filler = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"filler{i}"));

        var hits = await App.Client("ana").SearchAsync($"{filler} warehouse");

        Assert.Equal("warehouse-hours", Assert.Single(hits).Key);
    }

    [Fact]
    public async Task Search_MatchesStemmedKeyWords()
    {
        await App.SupervisorClient().PutCardAsync("company", "travel-policies", what: "Book economy", why: "Budget");

        var hits = await App.Client("ana").SearchAsync("policy");

        Assert.Equal("travel-policies", Assert.Single(hits).Key);
    }

    [Theory]
    [InlineData("see https://x.example/?a=1&b=2 refund")]
    [InlineData("AT&T refund | !( ) :* <-> 'quoted' \\ refund")]
    public async Task Search_HostileOrPunctuatedInput_StillMatches(string query)
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window");

        var hits = await App.Client("ana").SearchAsync(query);

        Assert.Equal("refund-window", Assert.Single(hits).Key);
    }

    [Fact]
    public async Task Search_OnlyStopWords_ReturnsNothing()
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window");

        Assert.Empty(await App.Client("ana").SearchAsync("the and of"));
    }

    [Fact]
    public async Task Search_Human_SeesCompanyAndOwnPersonal_Only()
    {
        await App.SupervisorClient().PutCardAsync("company", "company-refund");
        await App.Client("ana").PutCardAsync("personal:ana", "ana-refund");
        await App.Client("bob").PutCardAsync("personal:bob", "bob-refund");
        await App.Client("ana").PutCardAsync("team:billing", "team-refund");

        var hits = await App.Client("ana").SearchAsync("refund");

        Assert.Equal(["ana-refund", "company-refund"], hits.Select(h => h.Key).Order());
    }

    [Fact]
    public async Task GetCard_OtherUsersPersonal_Forbidden()
    {
        await App.Client("bob").PutCardAsync("personal:bob", "bob-refund");

        var response = await App.Client("ana").GetAsync("/api/memory/cards/personal:bob/bob-refund");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
