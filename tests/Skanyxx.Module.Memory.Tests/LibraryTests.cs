using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// The library's queries (what the Host's Library page sends): the list follows the read rules, company lists published
/// cards only (D048), lift targets are exactly the higher scopes the person may write (D2). Org: ana is in team billing
/// (department finance), bob in payroll (finance), carol in nothing; the supervisor in nothing.
/// </summary>
public sealed class LibraryTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private static readonly LibraryUser Ana = People.Person("ana");
    private static readonly LibraryUser Bob = People.Person("bob");
    private static readonly LibraryUser Carol = People.Person("carol");
    private static readonly LibraryUser Boss = People.Person(MemoryApp.Supervisor, SkanyxxRoles.Supervisor);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join("ana", "billing", "finance");
        App.Org.Join("bob", "payroll", "finance");
        var boss = App.SupervisorClient();
        await boss.PutCardAsync("company", "refund-window", what: "Refunds within 14 days");
        await boss.PutCardAsync("company", "draft-policy", what: "Draft refund policy");
        await App.Client("ana").PutCardAsync("personal:ana", "ana-note", what: "Ana refund note");
        await App.Client("ana").PutCardAsync("team:billing", "billing-refunds", what: "Billing refunds by card");
        await App.Client("ana").PutCardAsync("team:billing", "billing-draft", what: "Billing draft");
        await App.Client("bob").PutCardAsync("team:payroll", "payroll-runs", what: "Payroll runs monthly");
        await App.Client("carol").PutCardAsync("personal:carol", "carol-note", what: "Carol note");
        await using var db = Postgres.CreateDbContext();
        foreach (var draft in db.Cards.Where(c => c.Key == "draft-policy" || c.Key == "billing-draft"))
            draft.Status = CardStatus.Candidate;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AnEmployeeInNoTeam_SeesPublishedCompanyAndTheirOwn_Only()
    {
        var shelf = (await SendAsync(new BrowseLibraryQuery(Carol, null, null))).Value!;

        Assert.Equal(["company", "personal:carol"], shelf.Scopes);
        Assert.Equal(["carol-note", "refund-window"], shelf.Cards.Select(c => c.Key).Order());
    }

    [Fact]
    public async Task AMember_SeesTheirTeamAndDepartment_WithNonPublishedStatus()
    {
        var shelf = (await SendAsync(new BrowseLibraryQuery(Ana, null, null))).Value!;

        Assert.Equal(["company", "personal:ana", "team:billing", "department:finance"], shelf.Scopes);
        Assert.Equal(["ana-note", "billing-draft", "billing-refunds", "refund-window"], shelf.Cards.Select(c => c.Key).Order());
        Assert.Equal("candidate", shelf.Cards.Single(c => c.Key == "billing-draft").Status);
        Assert.DoesNotContain(shelf.Cards, c => c.Key == "draft-policy"); // company: published only
    }

    [Fact]
    public async Task Text_MatchesAnyWord_InReadableScopesOnly()
    {
        var ana = (await SendAsync(new BrowseLibraryQuery(Ana, "refunds", null))).Value!;
        var bob = (await SendAsync(new BrowseLibraryQuery(Bob, "refunds payroll", null))).Value!;
        var nothing = (await SendAsync(new BrowseLibraryQuery(Ana, "!!! ???", null))).Value!;

        Assert.Equal(["ana-note", "billing-refunds", "refund-window"], ana.Cards.Select(c => c.Key).Order());
        Assert.Equal(["payroll-runs", "refund-window"], bob.Cards.Select(c => c.Key).Order());
        Assert.Empty(nothing.Cards);
    }

    [Fact]
    public async Task Filter_NarrowsToOneReadableScope_AndRefusesAnUnreadableOne()
    {
        var team = await SendAsync(new BrowseLibraryQuery(Ana, null, "team:billing"));
        var otherTeam = await SendAsync(new BrowseLibraryQuery(Ana, null, "team:payroll"));
        var otherPersonal = await SendAsync(new BrowseLibraryQuery(Carol, null, "personal:ana"));

        Assert.Equal(["billing-draft", "billing-refunds"], team.Value!.Cards.Select(c => c.Key).Order());
        Assert.Equal(OutcomeStatus.Forbidden, otherTeam.Status);
        Assert.Equal(OutcomeStatus.Forbidden, otherPersonal.Status);
        await Assert.ThrowsAsync<ValidationException>(() => SendAsync(new BrowseLibraryQuery(Ana, null, "nonsense")));
    }

    /// <summary>Oversight (D090): every team and department scope that holds cards, member or not.</summary>
    [Fact]
    public async Task ASupervisor_BrowsesEveryTeamScopeThatHasCards()
    {
        var shelf = (await SendAsync(new BrowseLibraryQuery(Boss, null, null))).Value!;

        Assert.Equal(["company", "personal:boss", "team:billing", "team:payroll"], shelf.Scopes);
        Assert.Contains(shelf.Cards, c => c.Key == "payroll-runs");
        Assert.DoesNotContain(shelf.Cards, c => c.Scope.StartsWith("personal:") && c.Scope != "personal:boss");
    }

    [Fact]
    public async Task Open_OffersOnlyHigherScopesThePersonMayWrite()
    {
        await App.SupervisorClient().PutCardAsync("personal:boss", "boss-note");

        var anaPersonal = (await SendAsync(new OpenCardQuery(Ana, "personal:ana", "ana-note"))).Value!;
        var anaTeam = (await SendAsync(new OpenCardQuery(Ana, "team:billing", "billing-refunds"))).Value!;
        var anaCompany = (await SendAsync(new OpenCardQuery(Ana, "company", "refund-window"))).Value!;
        var bossPersonal = (await SendAsync(new OpenCardQuery(Boss, "personal:boss", "boss-note"))).Value!;
        var bossTeam = (await SendAsync(new OpenCardQuery(Boss, "team:billing", "billing-refunds"))).Value!;
        var draft = (await SendAsync(new OpenCardQuery(Ana, "team:billing", "billing-draft"))).Value!;

        Assert.Equal(["team:billing", "department:finance"], anaPersonal.LiftTargets);
        Assert.True(anaPersonal.CanRename);
        Assert.Equal(["department:finance"], anaTeam.LiftTargets);
        Assert.True(anaTeam.CanRename);
        Assert.Empty(anaCompany.LiftTargets);
        Assert.False(anaCompany.CanRename);
        Assert.Equal(["company"], bossPersonal.LiftTargets);
        Assert.True(bossPersonal.CanRename);
        Assert.Equal(["company"], bossTeam.LiftTargets);
        Assert.False(bossTeam.CanRename); // oversight: lift to company, no team write
        Assert.Empty(draft.LiftTargets); // only published cards are lifted
        Assert.Equal("Billing refunds by card", anaTeam.Card.What);
    }

    [Fact]
    public async Task ASupervisorInATeam_MayLiftIntoItAndIntoCompany()
    {
        App.Org.Join(MemoryApp.Supervisor, "billing", "finance");
        await App.SupervisorClient().PutCardAsync("personal:boss", "boss-note");

        var opened = (await SendAsync(new OpenCardQuery(Boss, "personal:boss", "boss-note"))).Value!;

        Assert.Equal(["team:billing", "department:finance", "company"], opened.LiftTargets);
    }

    [Fact]
    public async Task Open_FollowsTheReadRule()
    {
        var otherTeam = await SendAsync(new OpenCardQuery(Carol, "team:billing", "billing-refunds"));
        var otherPersonal = await SendAsync(new OpenCardQuery(Carol, "personal:ana", "ana-note"));
        var missing = await SendAsync(new OpenCardQuery(Carol, "company", "nothing"));

        Assert.Equal(OutcomeStatus.Forbidden, otherTeam.Status);
        Assert.Equal(OutcomeStatus.Forbidden, otherPersonal.Status);
        Assert.Equal(OutcomeStatus.NotFound, missing.Status);
    }

    /// <summary>A copy names its source only to someone who could open the source.</summary>
    [Fact]
    public async Task LiftedFrom_IsNamedOnlyToThoseWhoMayReadTheSource()
    {
        await App.Client("ana").LiftAsync("personal:ana", "ana-note", "team:billing");
        await App.Client("ana").LiftAsync("team:billing", "ana-note", "department:finance");

        var anaTeamCopy = (await SendAsync(new OpenCardQuery(Ana, "team:billing", "ana-note"))).Value!;
        var bobDepartmentCopy = (await SendAsync(new OpenCardQuery(Bob, "department:finance", "ana-note"))).Value!;

        Assert.Equal("personal:ana/ana-note", anaTeamCopy.LiftedFrom);
        Assert.Null(bobDepartmentCopy.LiftedFrom);
        Assert.NotNull(bobDepartmentCopy.Card.LiftedFromId);
    }

    [Fact]
    public async Task WritableScopes_AreExactlyWhatCanUpsertAllows()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<Access.AccessPolicy>();
        List<Scope> all =
        [
            Scope.Company, Scope.Personal("ana"), Scope.Personal("bob"), Scope.Parse("team:billing"), Scope.Parse("team:payroll"),
            Scope.Parse("department:finance"), Scope.Parse("department:ops")
        ];

        foreach (var caller in new[] { Ana, Bob, Carol, Boss }.Select(MemoryCaller.For))
        {
            var writable = await access.WritableScopesAsync(caller, TestContext.Current.CancellationToken);
            foreach (var candidate in all)
                Assert.Equal(await access.CanUpsertAsync(caller, candidate, TestContext.Current.CancellationToken), writable.Contains(candidate));
        }
    }

    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }
}
