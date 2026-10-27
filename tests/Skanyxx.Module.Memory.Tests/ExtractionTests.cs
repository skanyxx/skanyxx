using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>memory-hierarchy.md: cards are born personal. A chat session and a ticket run each become ONE small card.</summary>
public sealed class ExtractionTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private static ChatTurnDigest Turn(string message, string reply, bool first = true, string session = "s-1", string user = "ana") =>
        new(user, "seed", "kagent/seed", session, message, reply, first);

    [Fact]
    public async Task AChatSession_IsOneSmallPersonalCard_WithTicketsAndAPointer_NeverTheTranscript()
    {
        var transcript = string.Concat(Enumerable.Repeat("Long explanation of the refund flow. ", 400));

        var card = await App.ChatTurnAsync(Turn("How do refunds work for SDB-58 in the EU?", "Refunds take 14 days. " + transcript));

        Assert.NotNull(card);
        Assert.Equal("personal:ana", card.Scope);
        await using var db = Postgres.CreateDbContext();
        var stored = await db.Cards.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CardOrigin.Chat, stored.Origin);
        Assert.Equal("chat:s-1", stored.Source);
        Assert.Equal(["SDB-58"], stored.Tickets);
        Assert.Contains("refunds", stored.Keywords);
        Assert.Equal("ana via seed", stored.Who);
        Assert.Equal("seed", stored.AgentId);
        Assert.True(stored.What.Length <= CardLimits.What);
        Assert.True(stored.Why.Length <= CardLimits.Why);
        Assert.Null(stored.Body);
        Assert.StartsWith("Refunds take 14 days.", stored.Why);
    }

    [Fact]
    public async Task LaterTurns_UpdateTheSameCard_KeepTheTopic_AndCollectTickets()
    {
        await App.ChatTurnAsync(Turn("How do refunds work for SDB-58?", "Within 14 days."));
        var second = await App.ChatTurnAsync(Turn("And for SDB-64?", "Same as SDB-58, but EU needs a form.", first: false));

        Assert.NotNull(second);
        await using var db = Postgres.CreateDbContext();
        var card = await db.Cards.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, card.Version);
        Assert.Contains("How do refunds work", card.What);
        Assert.StartsWith("Same as SDB-58", card.Why);
        Assert.Equal(["SDB-58", "SDB-64"], card.Tickets);
        Assert.Single(db.CardVersions); // the first version is kept
    }

    [Fact]
    public async Task ARepeatedIdenticalTurn_WritesNoNewVersion()
    {
        await App.ChatTurnAsync(Turn("How do refunds work for SDB-58?", "Within 14 days."));
        await App.ChatTurnAsync(Turn("How do refunds work for SDB-58?", "Within 14 days.", first: false));

        await using var db = Postgres.CreateDbContext();
        Assert.Equal(1, (await db.Cards.SingleAsync(TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task SmallTalk_KeepsNoCard()
    {
        Assert.Null(await App.ChatTurnAsync(Turn("hi", "Hello! How can I help?")));
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task AnotherPerson_NeverSeesTheSessionCard()
    {
        await App.ChatTurnAsync(Turn("Private salary question about payroll", "Your salary review is in March."));

        Assert.Empty(await App.Client("bob").SearchAsync("salary payroll"));
        Assert.Contains("salary", (await App.Client("ana").SearchAsync("salary")).Single().What, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await App.RelatedAsync("bob", "seed", "salary payroll")).Cards);
    }

    [Fact]
    public async Task InvalidIdentities_KeepNothing()
    {
        Assert.Null(await App.ChatTurnAsync(Turn("How do refunds work here?", "Within 14 days.", user: "Not A User")));
        Assert.Null(await App.ChatTurnAsync(Turn("How do refunds work here?", "Within 14 days.", session: "bad id/../x")));
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task AFinishedRun_IsACardInItsOwnersPersonalScope_FoundByTicketLater()
    {
        var run = new TicketRunFinished(
            Guid.NewGuid(), "ana", "SDB-58", "Refund form breaks for EU", "https://jira.example/browse/SDB-58", "default",
            Succeeded: true, "Review (pass): the EU form now validates the IBAN.", ["SDB-12"], ["Billing", "eu"]);

        var card = await App.TicketRunAsync(run);

        Assert.NotNull(card);
        Assert.Equal("personal:ana", card.Scope);
        await using var db = Postgres.CreateDbContext();
        var stored = await db.Cards.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CardOrigin.Run, stored.Origin);
        Assert.Equal($"run:{run.RunId}", stored.Source);
        Assert.Equal(["SDB-58", "SDB-12"], stored.Tickets);
        Assert.Equal(["https://jira.example/browse/SDB-58"], stored.Links);
        Assert.Contains("billing", stored.Keywords);
        Assert.Contains("succeeded", stored.What);

        // Ticket 64's team member (ana herself here) finds ticket 58's run by its key.
        var hits = await App.Client("ana").SearchAsync("what happened on sdb-58");
        Assert.Equal(card.Key, hits.First().Key);
    }

    [Fact]
    public async Task ARefusedExtraction_AtTheQuota_DeletesNothing()
    {
        // C-2: the second session is refused by the write limiter; the first session's card must survive it.
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString, upsertsPerMinute: 1,
            extra: new Dictionary<string, string?> { ["Memory:MaxCardsPerPersonalScope"] = "1" });
        Assert.NotNull(await app.ChatTurnAsync(Turn("First session about refunds", "Answer one.", session: "s-1")));

        var second = await app.ChatTurnAsync(Turn("Second session about invoices", "Answer two.", session: "s-2"));

        Assert.Null(second);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(["chat-s-1"], await db.Cards.Select(c => c.Key).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFullPersonalScope_DropsItsOldestExtractedCard_NeverAHandWrittenOne()
    {
        await using var app = await MemoryApp.StartAsync(Postgres.ConnectionString,
            extra: new Dictionary<string, string?> { ["Memory:MaxCardsPerPersonalScope"] = "2" });
        await app.Client("ana").PutCardAsync("personal:ana", "hand-written");
        await app.ChatTurnAsync(Turn("First session about refunds", "Answer one.", session: "s-1"));

        await app.ChatTurnAsync(Turn("Second session about invoices", "Answer two.", session: "s-2"));

        await using var db = Postgres.CreateDbContext();
        Assert.Equal(["chat-s-2", "hand-written"], await db.Cards.Select(c => c.Key).OrderBy(k => k).ToListAsync(TestContext.Current.CancellationToken));

        // Only hand-written cards left at the quota: nothing is dropped and nothing is kept.
        await app.Client("ana").PutCardAsync("personal:ana", "second-hand"); // refused: full
        await db.Cards.Where(c => c.Key == "chat-s-2").ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await app.Client("ana").PutCardAsync("personal:ana", "second-hand");
        Assert.Null(await app.ChatTurnAsync(Turn("Third session about payroll", "Answer three.", session: "s-3")));
    }
}
