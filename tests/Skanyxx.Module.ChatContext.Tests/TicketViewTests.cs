using System.Net.Http.Json;
using Skanyxx.Core.Platform.Tickets;
using Skanyxx.Module.ChatContext.Features;
using Skanyxx.Module.ChatContext.Tests.Infrastructure;
using static Skanyxx.Module.ChatContext.Tests.Infrastructure.People;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D240: a team on SDB-64 finds the chats and runs of SDB-58 on the same topic.</summary>
public sealed class TicketViewTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    private async Task<TicketView> ViewAsync(string userId, string ticket) =>
        (await App.Client(userId).GetFromJsonAsync<TicketView>($"/api/chat/tickets/{ticket}", ChatContextApp.Json))!;

    private static TicketRunSummary Run(string ticket) =>
        new(Guid.NewGuid(), ticket, "default", "succeeded", Ana, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task ATicket_ShowsItsChatsAndRuns_AndRelatedTicketsWithTheirRuns()
    {
        App.Org.Join(Ana, "billing", "finance");
        App.Org.Join(Bob, "billing", "finance");
        await App.TurnAsync(Ana, "on-58", "EU refund payouts are delayed by the bank batch", "Moved the batch to daily.", ["SDB-58"]);
        await App.ShareAsync(Ana, "on-58", "team:billing");
        await App.TurnAsync(Bob, "on-64", "EU refund payouts delayed again", "Investigating.", ["SDB-64"]);
        await App.TurnAsync(Bob, "unrelated", "Office plants", "Water them.", ["SDB-77"]);
        App.Runs.Runs.AddRange([Run("SDB-58"), Run("SDB-64"), Run("SDB-77")]);

        var view = await ViewAsync(Bob, "SDB-64");

        Assert.Equal(["on-64"], view.Sessions.Select(s => s.SessionId));
        Assert.Single(view.Runs);
        var related = Assert.Single(view.RelatedTickets);
        Assert.Equal("SDB-58", related.TicketKey);
        Assert.Single(related.Runs);

        var back = await ViewAsync(Bob, "SDB-58");
        Assert.Equal(["on-58"], back.Sessions.Select(s => s.SessionId));
        Assert.Contains(back.RelatedTickets, t => t.TicketKey == "SDB-64");
    }

    [Fact]
    public async Task TicketsOnTheSameSession_AreRelated()
    {
        await App.TurnAsync(Ana, "both", "Merge SDB-58 into SDB-64", "Merged.");

        var view = await ViewAsync(Ana, "SDB-58");

        Assert.Equal("SDB-64", Assert.Single(view.RelatedTickets).TicketKey);
    }

    [Fact]
    public async Task AnInvalidKey_IsRefused()
    {
        var response = await App.Client(Ana).GetAsync("/api/chat/tickets/not-a-key", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
