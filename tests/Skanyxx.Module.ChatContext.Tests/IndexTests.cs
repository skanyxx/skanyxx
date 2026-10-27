using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Tests.Infrastructure;
using static Skanyxx.Module.ChatContext.Tests.Infrastructure.People;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D236: every completed turn updates the session's row; only derived fields are kept.</summary>
public sealed class IndexTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    private async Task<Domain.ChatSession> RowAsync(string id)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChatContextDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    [Fact]
    public async Task TheFirstTurn_CreatesThePersonalRow_TitledByTheQuestion()
    {
        await App.TurnAsync(Ana, "s1", "How long is the refund window?\nAsking for Acme.", "Fourteen days, per policy X. Ask finance for more.", ["SDB-58"]);

        var row = await RowAsync("s1");
        Assert.Equal(Ana, row.UserId);
        Assert.Equal("kagent/seed", row.AgentRef);
        Assert.Equal("personal:ana", row.Scope);
        Assert.Equal("How long is the refund window?", row.Title);
        Assert.Equal(["SDB-58"], row.TicketKeys);
        Assert.Contains("refund", row.Keywords);
        Assert.Equal("Fourteen days, per policy X. Ask finance for more.", row.Outcome);
        Assert.Equal(1, row.Turns);
    }

    [Fact]
    public async Task LaterTurns_FoldIn_KeepTheTitle_AndMoveTheOutcome()
    {
        await App.TurnAsync(Ana, "s1", "Refund window?", "Fourteen days.");
        await App.TurnAsync(Ana, "s1", "And for gift cards, see OPS-7?", "Gift cards are not refundable.", ["SDB-58"], first: false);

        var row = await RowAsync("s1");
        Assert.Equal("Refund window?", row.Title);
        Assert.Equal(["SDB-58", "OPS-7"], row.TicketKeys);
        Assert.Contains("gift", row.Keywords);
        Assert.Equal("Gift cards are not refundable.", row.Outcome);
        Assert.Equal(2, row.Turns);
        Assert.True(row.UpdatedAt > row.CreatedAt);
    }

    [Fact]
    public async Task SearchFindsSessions_ByWords_Ticket_Category_AndAgent()
    {
        await App.TurnAsync(Ana, "s1", "Refund the invoice for Acme", "Refunded in the billing console.", ["SDB-58"]);
        await App.TurnAsync(Ana, "s2", "The helm deployment failed", "Rolled back the release.", agent: "team-a/seed");

        string[] Ids(List<System.Text.Json.JsonElement> found) => [.. found.Select(s => s.GetProperty("sessionId").GetString()!)];
        Assert.Equal(["s1"], Ids(await App.SearchAsync(Ana, "?q=invoices")));
        Assert.Equal(["s1"], Ids(await App.SearchAsync(Ana, "?ticket=SDB-58")));
        Assert.Equal(["s2"], Ids(await App.SearchAsync(Ana, "?category=deployment")));
        Assert.Equal(["s2"], Ids(await App.SearchAsync(Ana, "?agent=team-a/seed")));
        Assert.Equal(["s2", "s1"], Ids(await App.SearchAsync(Ana, "")));
    }

    [Fact]
    public async Task TicketLinks_CanBeReplacedByTheOwner()
    {
        await App.TurnAsync(Ana, "s1", "Refund", "Done.", ["SDB-58"]);

        var ct = TestContext.Current.CancellationToken;
        var response = await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/s1/tickets", new { ticketKeys = new[] { "SDB-64", "SDB-64" } }, ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal(["SDB-64"], (await RowAsync("s1")).TicketKeys);
        var invalid = await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/s1/tickets", new { ticketKeys = new[] { "not a key" } }, ct);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task AnEmojiAtTheCut_IsKeptWhole()
    {
        var title = new string('a', ChatContext.Context.ChatContextService.MaxTitle - 2) + "😀😀😀";
        var outcome = new string('b', Domain.ChatSession.MaxOutcome - 2) + "😀😀😀";

        await App.TurnAsync(Ana, "s1", title, outcome);

        var row = await RowAsync("s1");
        Assert.EndsWith("…", row.Title);
        Assert.False(char.IsHighSurrogate(row.Title[^2]));
        Assert.EndsWith("…", row.Outcome);
    }

    [Fact]
    public async Task ARemovedTicketLink_StaysRemoved()
    {
        await App.TurnAsync(Ana, "s1", "Refund", "Done.", ["SDB-58"]);
        (await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/s1/tickets", new { ticketKeys = Array.Empty<string>() }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        await App.TurnAsync(Ana, "s1", "And now?", "Still done.", first: false);

        Assert.Empty((await RowAsync("s1")).TicketKeys);
    }

    [Fact]
    public async Task ForgettingASession_RemovesItsRowOnly()
    {
        await App.TurnAsync(Ana, "s1", "Refund", "Done.");

        (await App.Client(Ana).DeleteAsync("/api/chat/sessions/s1", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        Assert.Empty(await App.SearchAsync(Ana, ""));
    }
}
