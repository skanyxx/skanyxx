using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D103 (clarifies D048): a company card that is not published opens only for supervisors/the owner (who may write company),
/// in the library and through <c>GET api/memory/cards/{scope}/{key}</c> alike. Anyone else gets exactly what a missing
/// card gets, so a guessed key tells them nothing.
/// </summary>
public sealed class CompanyDraftTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Author = "sam"; // a supervisor when they wrote it, an employee now: last writer grants nothing

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await App.Client(Author, SkanyxxRoles.Supervisor).PutCardAsync("company", "draft-policy", what: "Draft refund policy");
        await App.Client(Author, SkanyxxRoles.Supervisor).PutCardAsync("company", "stale-policy", what: "Old refund policy");
        await App.Client(Author, SkanyxxRoles.Supervisor).PutCardAsync("company", "refund-window", what: "Refunds within 14 days");
        await using var db = Postgres.CreateDbContext();
        db.Cards.Single(c => c.Key == "draft-policy").Status = CardStatus.Candidate;
        db.Cards.Single(c => c.Key == "stale-policy").Status = CardStatus.Stale;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("draft-policy")]
    [InlineData("stale-policy")]
    public async Task AnEmployee_GetsTheMissingCardAnswer(string key)
    {
        var ana = App.Client("ana");

        var draft = await ana.GetAsync($"/api/memory/cards/company/{key}", TestContext.Current.CancellationToken);
        var missing = await ana.GetAsync("/api/memory/cards/company/no-such-card", TestContext.Current.CancellationToken);
        var opened = await SendAsync(new OpenCardQuery(People.Person("ana"), "company", key));

        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(Problem(await missing.JsonAsync()).Replace("no-such-card", key), Problem(await draft.JsonAsync()));
        Assert.Equal(OutcomeStatus.NotFound, opened.Status);
        Assert.Null(opened.Value);
        Assert.Equal($"No card 'company/{key}'.", opened.Message);
    }

    [Theory]
    [InlineData(MemoryApp.Supervisor, SkanyxxRoles.Supervisor)]
    [InlineData(MemoryApp.Owner, SkanyxxRoles.Owner)]
    public async Task SupervisorsAndTheOwner_OpenIt(string user, string role)
    {
        var get = await App.Client(user, role).GetAsync("/api/memory/cards/company/draft-policy", TestContext.Current.CancellationToken);
        var opened = await SendAsync(new OpenCardQuery(People.Person(user, role), "company", "draft-policy"));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("candidate", (await get.CardAsync()).Status);
        Assert.Equal("Draft refund policy", opened.Value!.Card.What);
    }

    [Fact]
    public async Task ItsLastWriter_NoLongerASupervisor_GetsTheMissingCardAnswer()
    {
        var get = await App.Client(Author).GetAsync("/api/memory/cards/company/draft-policy", TestContext.Current.CancellationToken);
        var opened = await SendAsync(new OpenCardQuery(People.Person(Author), "company", "draft-policy"));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(OutcomeStatus.NotFound, opened.Status);
    }

    [Fact]
    public async Task PublishedCompanyCards_AndOtherScopesDrafts_AreUnchanged()
    {
        App.Org.Join("ana", "billing", "finance");
        App.Org.Join("bob", "billing", "finance");
        await App.Client("ana").PutCardAsync("team:billing", "billing-draft");
        await using (var db = Postgres.CreateDbContext())
        {
            db.Cards.Single(c => c.Key == "billing-draft").Status = CardStatus.Candidate;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.OK, (await App.Client("bob").GetAsync("/api/memory/cards/company/refund-window", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.Client("bob").GetAsync("/api/memory/cards/team:billing/billing-draft", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>Everything a problem body says except its per-request trace id.</summary>
    private static string Problem(System.Text.Json.JsonElement body) =>
        string.Join("|", body.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));

    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }
}
