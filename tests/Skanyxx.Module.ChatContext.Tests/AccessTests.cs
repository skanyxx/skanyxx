using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.ChatContext.Tests.Infrastructure;
using static Skanyxx.Module.ChatContext.Tests.Infrastructure.People;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>
/// D237: a person sees, and is injected with, only sessions they may read. Another person's private chat never shows up:
/// not in search, history, by id, in a ticket view, in related tickets, nor in related work for a new chat.
/// </summary>
public sealed class AccessTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    private const string Refund = "How long is the refund window for EU customers on ticket SDB-58?";
    private const string Answer = "The refund window for EU customers is 14 days, per policy X.";

    [Fact]
    public async Task AnotherPersonsPrivateSession_NeverLeaks()
    {
        await App.TurnAsync(Ana, "ana-private", Refund, Answer, ["SDB-58", "SDB-99"]);
        await App.TurnAsync(Bob, "bob-own", "Refund for SDB-64 customers?", "Unknown yet.", ["SDB-64"]);

        Assert.Equal(["bob-own"], (await App.SearchAsync(Bob, "?q=refund+window+EU")).Select(s => s.GetProperty("sessionId").GetString()));
        Assert.DoesNotContain(await App.SearchAsync(Bob, ""), s => s.GetProperty("sessionId").GetString() == "ana-private");
        Assert.Empty(await App.SearchAsync(Bob, "?ticket=SDB-58"));
        Assert.Equal(HttpStatusCode.NotFound, (await App.Client(Bob).GetAsync("/api/chat/sessions/ana-private", TestContext.Current.CancellationToken)).StatusCode);

        var view = await App.Client(Bob).GetFromJsonAsync<Features.TicketView>("/api/chat/tickets/SDB-58", ChatContextApp.Json, TestContext.Current.CancellationToken);
        Assert.Empty(view!.Sessions);
        Assert.Empty(view.RelatedTickets);
        // Ana's private session links SDB-58 to SDB-99 and shares Bob's words: neither may surface for Bob.
        var own = await App.Client(Bob).GetFromJsonAsync<Features.TicketView>("/api/chat/tickets/SDB-64", ChatContextApp.Json, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(own!.RelatedTickets, t => t.TicketKey is "SDB-58" or "SDB-99");

        var prepared = await App.PrepareAsync(Bob, Refund, ["SDB-58"]);
        Assert.DoesNotContain(prepared.Sessions, s => s.SessionId == "ana-private");
        Assert.DoesNotContain("14 days", prepared.Context);
    }

    [Fact]
    public async Task APersonalSession_IsHiddenFromSupervisorsAndTheOwnerToo()
    {
        await App.TurnAsync(Ana, "ana-private", Refund, Answer, ["SDB-58"]);

        foreach (var reader in new[] { Boss, Olivia })
        {
            Assert.Empty(await App.SearchAsync(reader, "?q=refund"));
            Assert.Equal(HttpStatusCode.NotFound, (await App.Client(reader).GetAsync("/api/chat/sessions/ana-private", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Empty((await App.PrepareAsync(reader, Refund, ["SDB-58"])).Sessions);
        }
    }

    [Fact]
    public async Task ATeamSession_IsReadByMembersAndSupervisors_NotByOthers()
    {
        App.Org.Join(Ana, "billing", "finance");
        App.Org.Join(Bob, "billing", "finance");
        App.Org.Join(Cleo, "support", "service");
        await App.TurnAsync(Ana, "ana-shared", Refund, Answer, ["SDB-58"]);
        await App.ShareAsync(Ana, "ana-shared", "team:billing");

        Assert.Single(await App.SearchAsync(Bob, "?q=refund"));
        Assert.Single(await App.SearchAsync(Boss, "?q=refund"));
        Assert.Empty(await App.SearchAsync(Cleo, "?q=refund"));
        Assert.Contains((await App.PrepareAsync(Bob, Refund, ["SDB-58"])).Sessions, s => s.SessionId == "ana-shared" && !s.Mine);
        Assert.Empty((await App.PrepareAsync(Cleo, Refund, ["SDB-58"])).Sessions);

        // Taken back: private again.
        await App.ShareAsync(Ana, "ana-shared", "personal:ana");
        Assert.Empty(await App.SearchAsync(Bob, "?q=refund"));
    }

    [Fact]
    public async Task ADepartmentSession_IsReadByTheDepartmentsTeams()
    {
        App.Org.Join(Ana, "billing", "finance");
        App.Org.Join(Bob, "payroll", "finance");
        App.Org.Join(Cleo, "support", "service");
        await App.TurnAsync(Ana, "ana-dept", Refund, Answer);
        await App.ShareAsync(Ana, "ana-dept", "department:finance");

        Assert.Single(await App.SearchAsync(Bob, "?q=refund"));
        Assert.Empty(await App.SearchAsync(Cleo, "?q=refund"));
    }

    [Fact]
    public async Task ACompanySession_IsReadByEveryone_ButOnlyASupervisorMayShareThere()
    {
        await App.TurnAsync(Ana, "ana-chat", Refund, Answer);
        await App.TurnAsync(Boss, "boss-chat", "Refund policy for the company?", "Fourteen days everywhere.");

        var refused = await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/ana-chat/scope", new { scope = "company" }, TestContext.Current.CancellationToken);
        await App.ShareAsync(Boss, "boss-chat", "company");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(["boss-chat"], (await App.SearchAsync(Cleo, "?q=refund")).Select(s => s.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task SharingNeedsMembership_AndOnlyTheOwnerChangesASession()
    {
        App.Org.Join(Bob, "billing", "finance");
        await App.TurnAsync(Ana, "ana-chat", Refund, Answer);

        var notMember = await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/ana-chat/scope", new { scope = "team:billing" }, TestContext.Current.CancellationToken);
        var someoneElses = await App.Client(Ana).PutAsJsonAsync("/api/chat/sessions/ana-chat/scope", new { scope = "personal:bob" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, notMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, someoneElses.StatusCode);

        // Unreadable to Bob: every change is "not found", so the id reveals nothing.
        var bob = App.Client(Bob);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync("/api/chat/sessions/ana-chat/tickets", new { ticketKeys = new[] { "SDB-1" } }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync("/api/chat/sessions/ana-chat/scope", new { scope = "personal:bob" }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync("/api/chat/sessions/ana-chat", TestContext.Current.CancellationToken)).StatusCode);

        // Readable but not his: forbidden.
        App.Org.Join(Ana, "billing", "finance");
        await App.ShareAsync(Ana, "ana-chat", "team:billing");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync("/api/chat/sessions/ana-chat/tickets", new { ticketKeys = new[] { "SDB-1" } }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync("/api/chat/sessions/ana-chat", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task History_IsOnlyOnesOwnSessions_EvenWithSharedOnesReadable()
    {
        await App.TurnAsync(Ana, "ana-chat", Refund, Answer);
        await App.TurnAsync(Boss, "boss-chat", "Refund policy?", "Fourteen days.");
        await App.ShareAsync(Boss, "boss-chat", "company");

        Assert.Equal(["ana-chat"], (await App.SearchAsync(Ana, "?mine=true")).Select(s => s.GetProperty("sessionId").GetString()));
        Assert.Equal(2, (await App.SearchAsync(Ana, "")).Count);
    }

    [Fact]
    public async Task AnonymousCallers_AreRefused()
    {
        var anonymous = new HttpClient { BaseAddress = App.Client(Ana).BaseAddress };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/chat/sessions", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/chat/tickets/SDB-58", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/chat/cache", TestContext.Current.CancellationToken)).StatusCode);
    }
}
