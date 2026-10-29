using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D507: a person deletes their own personal cards (the Library page's command), the owner any card (also over the API);
/// nobody else, and never a stale version.
/// </summary>
public sealed class DeleteCardTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<OutcomeStatus> DeleteAsync(LibraryUser user, string scope, string key, int version)
    {
        await using var services = App.Services.CreateAsyncScope();
        return (await services.ServiceProvider.GetRequiredService<IMediator>().Send(new DeleteCardCommand(user, scope, key, version), Ct)).Status;
    }

    [Fact]
    public async Task APerson_DeletesTheirOwnCard_WithItsHistory_AndItLeavesSearch()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-note", what: "Ana refunds by hand");
        await ana.PutCardAsync("personal:ana", "refund-note", version: 1, what: "Ana refunds by hand, twice");
        Assert.NotEmpty(await ana.SearchAsync("refunds"));

        var overApi = await ana.DeleteAsync("/api/memory/cards/personal:ana/refund-note?version=2", Ct);
        var stale = await DeleteAsync(People.Person("ana"), "personal:ana", "refund-note", 1);
        var deleted = await DeleteAsync(People.Person("ana"), "personal:ana", "refund-note", 2);

        Assert.Equal(HttpStatusCode.Forbidden, overApi.StatusCode);
        Assert.Equal(OutcomeStatus.Conflict, stale);
        Assert.Equal(OutcomeStatus.Ok, deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/memory/cards/personal:ana/refund-note", Ct)).StatusCode);
        Assert.Empty(await ana.SearchAsync("refunds"));
        await using var db = Postgres.CreateDbContext();
        Assert.Empty(await db.Cards.ToListAsync(Ct));
        Assert.Empty(await db.CardVersions.ToListAsync(Ct));
    }

    [Fact]
    public async Task ASharedCard_IsTheOwnersToDelete_NotItsWritersOrASupervisors()
    {
        App.Org.Join("ana", "billing", "finance");
        await App.Client("ana").PutCardAsync("team:billing", "queue", what: "Billing team refund queue");
        await App.SupervisorClient().PutCardAsync("company", "policy", what: "Company refunds within 14 days");

        Assert.Equal(OutcomeStatus.Forbidden, await DeleteAsync(People.Person("ana"), "team:billing", "queue", 1));
        Assert.Equal(OutcomeStatus.Forbidden, await DeleteAsync(People.Person(MemoryApp.Supervisor, SkanyxxRoles.Supervisor), "company", "policy", 1));
        Assert.Equal(OutcomeStatus.Forbidden, await DeleteAsync(People.Person("bob"), "personal:ana", "x", 1));
        Assert.Equal(HttpStatusCode.Forbidden, (await App.SupervisorClient().DeleteAsync("/api/memory/cards/company/policy?version=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.OwnerClient().DeleteAsync("/api/memory/cards/team:billing/queue?version=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.OwnerClient().DeleteAsync("/api/memory/cards/company/policy?version=1", Ct)).StatusCode);
    }

    [Fact]
    public async Task ALiftedCopy_StaysWhenItsOriginalIsDeleted()
    {
        App.Org.Join("ana", "billing", "finance");
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        Assert.Equal(HttpStatusCode.Created, (await ana.LiftAsync("personal:ana", "refund-window", "team:billing")).StatusCode);

        Assert.Equal(OutcomeStatus.Ok, await DeleteAsync(People.Person("ana"), "personal:ana", "refund-window", 1));

        await using var db = Postgres.CreateDbContext();
        var copy = Assert.Single(await db.Cards.ToListAsync(Ct));
        Assert.Equal("team:billing", copy.Scope);
        Assert.Null(copy.LiftedFromId);
    }

    [Fact]
    public async Task AnAgent_CannotDelete()
    {
        var secret = await App.IssueSecretAsync("seed");
        await App.Client("ana").PutCardAsync("personal:ana", "note");
        var agent = App.Client();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", secret);

        var refused = await agent.DeleteAsync("/api/memory/cards/personal:ana/note?version=1", Ct);

        Assert.True(refused.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, refused.StatusCode.ToString());
        await using var db = Postgres.CreateDbContext();
        Assert.Single(await db.Cards.ToListAsync(Ct));
    }
}
