using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// QA round 1 of D090 (D091): team and department grants are the owner's; grant changes and oversight lifts are
/// audited; an agent never reads through membership; a person's refusal is worded for a person; membership is asked
/// once per request. ana is in team billing (department finance); the supervisor and owner are in nothing.
/// </summary>
public sealed class OrgGrantTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Team = "team:billing";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join(Users.Ana, "billing", "finance");
    }

    [Theory]
    [InlineData("team:billing", true, false)]
    [InlineData("team:billing", false, true)]
    [InlineData("department:finance", true, false)]
    [InlineData("department:finance", false, true)]
    public async Task Supervisor_CannotGrantATeamOrDepartment_SearchOrUpsert(string scope, bool canSearch, bool canUpsert)
    {
        var response = await App.SupervisorClient().SetGrantsAsync("x",
            new { scope = "company", canSearch = true, canUpsert = false }, new { scope, canSearch, canUpsert });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Only the owner", (await response.JsonAsync()).GetProperty("detail").GetString());
        await using var db = Postgres.CreateDbContext();
        Assert.Empty(db.Grants);
    }

    [Fact]
    public async Task Supervisor_KeepsCompanyAndPersonalGrants_TheOwnerSetsTeamOnes()
    {
        var supervisor = await App.SupervisorClient().SetGrantsAsync("x",
            new { scope = "company", canSearch = true, canUpsert = true }, new { scope = "personal", canSearch = true, canUpsert = true });
        var owner = await App.OwnerClient().SetGrantsAsync("y",
            new { scope = Team, canSearch = true, canUpsert = true }, new { scope = "department:finance", canSearch = true, canUpsert = false });

        Assert.Equal(HttpStatusCode.OK, supervisor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    /// <summary>Replacing is removing, and a new secret is the agent's access: neither is a supervisor's once the owner granted a team.</summary>
    [Fact]
    public async Task Supervisor_CannotReplaceOrTakeOverAnAgentHoldingATeamGrant()
    {
        await App.OwnerClient().SetGrantsAsync("y", new { scope = Team, canSearch = true, canUpsert = false });

        var replace = await App.SupervisorClient().SetGrantsAsync("y", new { scope = "company", canSearch = true, canUpsert = false });
        var secret = await App.SupervisorClient().PostAsJsonAsync("/api/memory/agents/y/secret", new { actsForUsers = false }, cancellationToken: TestContext.Current.CancellationToken);
        var ownerSecret = await App.OwnerClient().PostAsJsonAsync("/api/memory/agents/y/secret", new { actsForUsers = false }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, replace.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, secret.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerSecret.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal([Team], db.Grants.Select(g => g.Scope).ToList());
        Assert.Single(db.AgentSecrets);
    }

    [Fact]
    public async Task GrantChanges_AreAuditedAtWarning_WithActorAgentScopesAndAddress()
    {
        await App.OwnerClient().SetGrantsAsync("y", new { scope = Team, canSearch = true, canUpsert = false });
        await App.SupervisorClient().SetGrantsAsync("x", new { scope = "department:finance", canSearch = false, canUpsert = true });

        var warnings = App.Logs.Split('\n').Where(l => l.StartsWith("Warning Agent memory grants")).ToList();

        Assert.Equal(2, warnings.Count);
        Assert.Contains($"grants set for y by {MemoryApp.Owner} from 127.0.0.1: team:billing (search: True, upsert: False)", warnings[0]);
        Assert.Contains($"grants refused for x by {MemoryApp.Supervisor} from 127.0.0.1: department:finance (search: False, upsert: True); reason: Only the owner", warnings[1]);
    }

    [Fact]
    public async Task ALiftOutOfATeamByANonMember_IsAudited_AMembersIsNot()
    {
        await App.Client(Users.Ana).PutCardAsync(Team, "refund-window");

        var member = await App.Client(Users.Ana).LiftAsync(Team, "refund-window", "department:finance");
        var supervisor = await App.SupervisorClient().LiftAsync(Team, "refund-window", "company");

        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        Assert.Equal(HttpStatusCode.Created, supervisor.StatusCode);
        var lifts = App.Logs.Split('\n').Where(l => l.StartsWith("Warning Card ")).ToList();
        Assert.Equal([$"Warning Card refund-window lifted from team:billing to company by {MemoryApp.Supervisor} from 127.0.0.1, who is not a member of the source"],
            lifts.Select(l => l[..l.IndexOf(" Key=", StringComparison.Ordinal)]));
    }

    /// <summary>
    /// D092 (CR MJ1, SEC N1): a secret a supervisor issued before the owner's team grant would carry the team to them;
    /// the grant is refused until the owner rotates, and the rotation kills the supervisor's copy.
    /// </summary>
    [Fact]
    public async Task ASecretSomeoneElseIssued_BlocksTheOwnersTeamGrant_UntilTheOwnerRotates()
    {
        var supervisorsSecret = await App.IssueSecretAsync("x");

        var refused = await App.OwnerClient().SetGrantsAsync("x", new { scope = Team, canSearch = true, canUpsert = true });
        await using (var db = Postgres.CreateDbContext())
            Assert.Empty(db.Grants);
        await App.IssueSecretAsync("x", byOwner: true);
        var granted = await App.OwnerClient().SetGrantsAsync("x", new { scope = Team, canSearch = true, canUpsert = true });
        var oldSecret = await App.PostMcpAsync("tools/list", $"Bearer {supervisorsSecret}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Rotate this agent's secret first: it was issued by someone else, and team/department grants are the owner's.",
            (await refused.JsonAsync()).GetProperty("message").GetString());
        Assert.Contains(App.Logs.Split('\n'), l => l.StartsWith($"Warning Agent memory grants refused for x by {MemoryApp.Owner} from 127.0.0.1") && l.Contains("reason: Rotate"));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSecret.StatusCode);
    }

    /// <summary>The owner's own secret, and grants that open no team, are not in the way.</summary>
    [Fact]
    public async Task TheOwnersOwnSecret_OrACompanyGrant_IsNotRefused()
    {
        await App.IssueSecretAsync("x", byOwner: true);
        await App.IssueSecretAsync("y");

        var team = await App.OwnerClient().SetGrantsAsync("x", new { scope = Team, canSearch = true, canUpsert = false });
        var company = await App.OwnerClient().SetGrantsAsync("y", new { scope = "company", canSearch = true, canUpsert = true });

        Assert.Equal(HttpStatusCode.OK, team.StatusCode);
        Assert.Equal(HttpStatusCode.OK, company.StatusCode);
    }

    /// <summary>CR NT1: an all-false row is how an agent is revoked; it opens no team, so it is nobody's to guard.</summary>
    [Fact]
    public async Task ARevokedTeamRow_IsNoTeamGrant_SupervisorsManageTheAgentAgain()
    {
        await App.OwnerClient().SetGrantsAsync("y", new { scope = Team, canSearch = true, canUpsert = false });
        await App.OwnerClient().SetGrantsAsync("y", new { scope = Team, canSearch = false, canUpsert = false });

        var secret = await App.SupervisorClient().PostAsJsonAsync("/api/memory/agents/y/secret", new { actsForUsers = false }, cancellationToken: TestContext.Current.CancellationToken);
        var replace = await App.SupervisorClient().SetGrantsAsync("y",
            new { scope = "company", canSearch = true, canUpsert = false }, new { scope = "department:finance", canSearch = false, canUpsert = false });

        Assert.Equal(HttpStatusCode.OK, secret.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
    }

    /// <summary>CR MN1 / SEC I3: the oversight audit is decided before the write, so a failed lookup leaves nothing committed.</summary>
    [Fact]
    public async Task ASupervisorsLift_WhoseMembershipCannotBeAsked_WritesNothing()
    {
        await App.Client(Users.Ana).PutCardAsync(Team, "refund-window");
        App.Org.Unavailable = true;

        var lift = await App.SupervisorClient().LiftAsync(Team, "refund-window", "company");

        Assert.Equal(HttpStatusCode.InternalServerError, lift.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.DoesNotContain(db.Cards, c => c.Scope == "company");
    }

    /// <summary>Verifier gap 1: every read check sends an agent through its grants, never through its user's membership.</summary>
    [Fact]
    public async Task AnAgentActingForAMember_ReadsNoTeamWithoutAGrant()
    {
        var agent = new MemoryCaller(Users.Ana, "seed");
        var scope = Scope.Parse(Team);
        App.Org.Lookups = 0;

        var withoutGrant = await CanReadAsync(agent, scope);
        await App.OwnerClient().SetGrantsAsync("seed", new { scope = Team, canSearch = true, canUpsert = false });
        var withGrant = await CanReadAsync(agent, scope);

        Assert.False(withoutGrant);
        Assert.True(withGrant);
        Assert.Equal(0, App.Org.Lookups);
        Assert.True(await CanReadAsync(new MemoryCaller(Users.Ana, null), scope));
    }

    /// <summary>Verifier gap 3: an agent lacks a grant; a person lacks membership, and is told so.</summary>
    [Fact]
    public async Task APersonsRefusedWrite_SaysWhoMayWrite()
    {
        var team = await App.Client(Users.Bob).PutCardAsync(Team, "x");
        var company = await App.Client(Users.Bob).PutCardAsync("company", "x");

        Assert.Equal("Only members of team:billing may write there.", (await team.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal("Only a supervisor may write to 'company'.", (await company.JsonAsync()).GetProperty("detail").GetString());
    }

    /// <summary>CR M1: one membership lookup per request, and none for a supervisor's search.</summary>
    [Fact]
    public async Task MembershipIsAskedOncePerRequest_AndNotForASupervisorsSearch()
    {
        await App.Client(Users.Ana).PutCardAsync(Team, "refund-window");

        App.Org.Lookups = 0;
        var lift = await App.Client(Users.Ana).LiftAsync(Team, "refund-window", "department:finance");
        var liftLookups = App.Org.Lookups;
        App.Org.Lookups = 0;
        var hits = await App.SupervisorClient().SearchAsync("refund");

        Assert.Equal(HttpStatusCode.Created, lift.StatusCode);
        Assert.Equal(1, liftLookups);
        Assert.Equal(2, hits.Count);
        Assert.Equal(0, App.Org.Lookups);
    }

    private async Task<bool> CanReadAsync(MemoryCaller caller, Scope scope)
    {
        await using var services = App.Services.CreateAsyncScope();
        return await services.ServiceProvider.GetRequiredService<AccessPolicy>().CanReadAsync(caller, scope, CancellationToken.None);
    }
}
