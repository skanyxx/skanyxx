using System.Net;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D055 in memory (D090): team and department scopes follow the org tree. Department <c>finance</c> holds teams
/// <c>billing</c> (ana) and <c>payroll</c> (bob); carol is in nothing; the supervisor and the owner are in nothing
/// either — they read every team/department scope but write only where they are members.
/// </summary>
public sealed class OrgAccessTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Team = "team:billing";
    private const string Department = "department:finance";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join("ana", "billing", "finance");
        App.Org.Join("bob", "payroll", "finance");
    }

    [Fact]
    public async Task Member_WritesReadsAndSearchesTheirTeam()
    {
        var write = await App.Client("ana").PutCardAsync(Team, "refund-window");

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("ana", Team, "refund-window")).StatusCode);
        Assert.Equal(["refund-window"], (await App.Client("ana").SearchAsync("refund")).Select(h => h.Key));
    }

    [Theory]
    [InlineData("carol")] // in no team
    [InlineData("bob")]   // another team of the same department
    public async Task NonMember_CannotReadSearchOrWriteTheTeam(string user)
    {
        await App.Client("ana").PutCardAsync(Team, "refund-window");

        var read = await GetAsync(user, Team, "refund-window");
        var hits = await App.Client(user).SearchAsync("refund");
        var write = await App.Client(user).PutCardAsync(Team, "other-card");

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Empty(hits);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task DepartmentMembership_ComesThroughTheTeams()
    {
        var write = await App.Client("ana").PutCardAsync(Department, "close-books");

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("bob", Department, "close-books")).StatusCode);
        Assert.Equal(["close-books"], (await App.Client("bob").SearchAsync("refund")).Select(h => h.Key));
        Assert.Equal(HttpStatusCode.Created, (await App.Client("bob").PutCardAsync(Department, "payroll-date")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("carol", Department, "close-books")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await App.Client("carol").PutCardAsync(Department, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await App.Client("ana").PutCardAsync("department:ops", "x")).StatusCode);
    }

    [Theory]
    [InlineData(MemoryApp.Supervisor, SkanyxxRoles.Supervisor)]
    [InlineData(MemoryApp.Owner, SkanyxxRoles.Owner)]
    public async Task SupervisorAndOwner_ReadAndSearchEveryTeamAndDepartment_ButWriteOnlyWhereMember(string user, string role)
    {
        await App.Client("ana").PutCardAsync(Team, "refund-window");
        await App.Client("bob").PutCardAsync(Department, "refund-close");
        var overseer = App.Client(user, role);

        var read = await overseer.GetAsync($"/api/memory/cards/{Team}/refund-window");
        var hits = await overseer.SearchAsync("refund");
        var writeTeam = await overseer.PutCardAsync(Team, "boss-card");
        var writeDepartment = await overseer.PutCardAsync(Department, "boss-card");
        var update = await overseer.PutCardAsync(Team, "refund-window", version: 1, what: "changed");
        App.Org.Join(user, "billing", "finance");
        var asMember = await overseer.PutCardAsync(Team, "boss-card");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(["refund-close", "refund-window"], hits.Select(h => h.Key).Order());
        Assert.Equal(HttpStatusCode.Forbidden, writeTeam.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, writeDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asMember.StatusCode);
    }

    /// <summary>A card whose slug has no org object (written before the tree existed, or a typo) has no members.</summary>
    [Theory]
    [InlineData("team:ghost")]
    [InlineData("department:ghost")]
    public async Task ACardInAScopeWithNoOrgObject_IsReadableByOwnerAndSupervisorOnly(string scope)
    {
        await InsertAsync(scope, "orphan-refund");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("ana", scope, "orphan-refund")).StatusCode);
        Assert.Empty(await App.Client("ana").SearchAsync("refund"));
        Assert.Equal(HttpStatusCode.Forbidden, (await App.Client("ana").PutCardAsync(scope, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.SupervisorClient().GetAsync($"/api/memory/cards/{scope}/orphan-refund")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.OwnerClient().GetAsync($"/api/memory/cards/{scope}/orphan-refund")).StatusCode);
        Assert.Equal(["orphan-refund"], (await App.SupervisorClient().SearchAsync("refund")).Select(h => h.Key));
    }

    [Fact]
    public async Task MembershipChanges_CountOnTheNextRequest()
    {
        await App.Client("ana").PutCardAsync(Team, "refund-window");
        var before = await GetAsync("ana", Team, "refund-window");

        App.Org.Leave("ana", "billing");
        var afterLeaving = await GetAsync("ana", Team, "refund-window");
        var searchAfterLeaving = await App.Client("ana").SearchAsync("refund");
        var writeAfterLeaving = await App.Client("ana").PutCardAsync(Team, "refund-window", version: 1);
        App.Org.Join("carol", "billing", "finance");
        var carolJoined = await GetAsync("carol", Team, "refund-window");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterLeaving.StatusCode);
        Assert.Empty(searchAfterLeaving);
        Assert.Equal(HttpStatusCode.Forbidden, writeAfterLeaving.StatusCode);
        Assert.Equal(HttpStatusCode.OK, carolJoined.StatusCode);
    }

    [Fact]
    public async Task MovingATeam_MovesItsMembersDepartment_OnTheNextRequest()
    {
        await App.Client("bob").PutCardAsync(Department, "close-books");
        App.Org.Join("ted", "tooling", "ops");
        await App.Client("ted").PutCardAsync("department:ops", "deploy-window");
        var before = await GetAsync("ana", Department, "close-books");

        App.Org.Team("billing", "ops");
        var oldDepartment = await GetAsync("ana", Department, "close-books");
        var newDepartment = await GetAsync("ana", "department:ops", "deploy-window");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, oldDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newDepartment.StatusCode);
    }

    [Fact]
    public async Task Lift_IntoATeam_NeedsMembershipOfIt()
    {
        await App.Client("ana").PutCardAsync("personal:ana", "refund-window");
        await App.Client("carol").PutCardAsync("personal:carol", "refund-window");

        var member = await App.Client("ana").LiftAsync("personal:ana", "refund-window", Team);
        var nonMember = await App.Client("carol").LiftAsync("personal:carol", "refund-window", Team);
        var supervisor = await App.SupervisorClient().LiftAsync(Team, "refund-window", "department:finance");

        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supervisor.StatusCode); // reads the team, but is not in the department
    }

    [Fact]
    public async Task Lift_FromATeam_NeedsToReadIt_AndTheTargetsRights()
    {
        await App.Client("ana").PutCardAsync(Team, "refund-window");

        var intoDepartment = await App.Client("ana").LiftAsync(Team, "refund-window", Department);
        var otherTeamSameDepartment = await App.Client("bob").LiftAsync(Team, "refund-window", Department);
        var memberIntoCompany = await App.Client("ana").LiftAsync(Team, "refund-window", "company");
        var supervisorIntoCompany = await App.SupervisorClient().LiftAsync(Team, "refund-window", "company");

        Assert.Equal(HttpStatusCode.Created, intoDepartment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherTeamSameDepartment.StatusCode);
        Assert.Contains("is not readable", (await otherTeamSameDepartment.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, memberIntoCompany.StatusCode);
        Assert.Equal(HttpStatusCode.Created, supervisorIntoCompany.StatusCode);
    }

    [Fact]
    public async Task Conflict_ShowsTheCurrentCard_ToAMember()
    {
        await App.Client("ana").PutCardAsync(Team, "refund-window");

        var conflict = await App.Client("ana").PutCardAsync(Team, "refund-window");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, (await conflict.JsonAsync()).GetProperty("current").GetProperty("version").GetInt32());
    }

    private Task<HttpResponseMessage> GetAsync(string user, string scope, string key) =>
        App.Client(user).GetAsync($"/api/memory/cards/{scope}/{key}");

    private async Task InsertAsync(string scope, string key)
    {
        await using var db = Postgres.CreateDbContext();
        db.Cards.Add(new Card
        {
            Scope = scope, Key = key, Version = 1, Type = CardType.Decision, What = "We refund within 14 days", Why = "Policy",
            Who = "legacy", UpdatedAt = DateTime.UtcNow, Status = CardStatus.Published
        });
        await db.SaveChangesAsync();
    }
}
