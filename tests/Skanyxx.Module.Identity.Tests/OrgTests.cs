using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D055 as built (D090): the owner builds the org tree — departments contain teams, people are members of teams — and
/// other modules read it live through <see cref="IOrgMembership"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrgTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly WarningLog _log = new();
    private IdentityApp _app = null!;
    private string _owner = null!;
    private string _ownerId = null!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
        _ownerId = (await postgres.OwnerAsync()).Id;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Owner_BuildsTheTree_AndListsIt()
    {
        var (_, bea) = await _app.AddMemberAsync(_owner);

        var department = await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" });
        var team = await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" });
        var added = await Owner().PutAsync($"/api/identity/org/teams/billing/members/{bea}", null);
        var departments = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/org/departments");
        var teams = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/org/teams");
        var beasTeams = await Owner().GetFromJsonAsync<JsonElement>($"/api/identity/people/{bea}/teams");
        var ownersTeams = await Owner().GetFromJsonAsync<JsonElement>($"/api/identity/people/{_ownerId}/teams");

        Assert.Equal(HttpStatusCode.Created, department.StatusCode);
        Assert.Equal(HttpStatusCode.Created, team.StatusCode);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal([bea], (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("""[{"slug":"finance","name":"Finance"}]""", departments.GetRawText());
        Assert.Equal($$"""[{"slug":"billing","name":"Billing","department":"finance","members":["{{bea}}"]}]""", teams.GetRawText());
        Assert.Equal(teams.GetRawText(), beasTeams.GetRawText());
        Assert.Equal(0, ownersTeams.GetArrayLength());
    }

    [Fact]
    public async Task Renames_KeepTheSlug_AndATeamMovesBetweenDepartments()
    {
        await TreeAsync();
        await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "ops", name = "Ops" });

        var renameDepartment = await Owner().PutAsJsonAsync("/api/identity/org/departments/finance", new { slug = "other", name = "Money" });
        var move = await Owner().PutAsJsonAsync("/api/identity/org/teams/billing", new { slug = "other", name = "Invoicing", department = "ops" });
        var teams = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/org/teams");
        var departments = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/org/departments");

        Assert.Equal("""{"slug":"finance","name":"Money"}""", (await renameDepartment.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal("""[{"slug":"billing","name":"Invoicing","department":"ops","members":[]}]""", teams.GetRawText());
        Assert.Equal(["finance", "ops"], departments.EnumerateArray().Select(d => d.GetProperty("slug").GetString()));
    }

    [Theory]
    [InlineData("Finance")]
    [InlineData("-finance")]
    [InlineData("fin ance")]
    [InlineData("finance\n")]
    [InlineData("")]
    public async Task BadSlugs_Are400(string slug)
    {
        var department = await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug, name = "Finance" });
        var team = await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug, name = "Billing", department = "finance" });

        Assert.Equal(HttpStatusCode.BadRequest, department.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, team.StatusCode);
    }

    /// <summary>The table holds the same line as the validator: every slug is a valid memory scope id.</summary>
    [Fact]
    public async Task TheDatabase_RefusesABadSlug_Too()
    {
        await using var db = postgres.CreateDbContext();
        db.Departments.Add(new Data.OrgDepartment { Slug = "Bad Slug", Name = "Bad", CreatedBy = _ownerId, CreatedUtc = DateTimeOffset.UtcNow });

        var error = await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Contains("CK_identity_org_departments_slug", error.InnerException!.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad\u202Ename")]
    public async Task BadNames_Are400(string name)
    {
        var response = await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ATakenSlug_Is409_PerKind()
    {
        await TreeAsync();

        var department = await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Again" });
        var team = await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Again", department = "finance" });
        var sameSlugOtherKind = await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "finance", name = "Finance team", department = "finance" });

        Assert.Equal(HttpStatusCode.Conflict, department.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, team.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sameSlugOtherKind.StatusCode);
        Assert.Empty(_log.Errors); // an expected answer, not a failed database write
    }

    [Fact]
    public async Task UnknownThings_Are404()
    {
        await TreeAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "t", name = "T", department = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PutAsJsonAsync("/api/identity/org/departments/nope", new { name = "N" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PutAsJsonAsync("/api/identity/org/teams/nope", new { name = "N", department = "finance" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PutAsJsonAsync("/api/identity/org/teams/billing", new { name = "N", department = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PutAsync($"/api/identity/org/teams/nope/members/{_ownerId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().PutAsync($"/api/identity/org/teams/billing/members/{Guid.NewGuid()}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner().GetAsync($"/api/identity/people/{Guid.NewGuid()}/teams")).StatusCode);
    }

    [Fact]
    public async Task Members_AreEnabledPeople_AndAddingOrRemovingTwiceIsHarmless()
    {
        await TreeAsync();
        var (_, bea) = await _app.AddMemberAsync(_owner);
        var (_, cal) = await _app.AddMemberAsync(_owner, "cal@skanyxx.example");
        await Owner().PostAsync($"/api/identity/people/{cal}/disable", null);

        var add = await Owner().PutAsync($"/api/identity/org/teams/billing/members/{bea}", null);
        var again = await Owner().PutAsync($"/api/identity/org/teams/billing/members/{bea}", null);
        var owner = await Owner().PutAsync($"/api/identity/org/teams/billing/members/{_ownerId}", null);
        var disabled = await Owner().PutAsync($"/api/identity/org/teams/billing/members/{cal}", null);
        var remove = await Owner().DeleteAsync($"/api/identity/org/teams/billing/members/{bea}");
        var removeAgain = await Owner().DeleteAsync($"/api/identity/org/teams/billing/members/{bea}");

        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        Assert.Contains("disabled", await disabled.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal([_ownerId], (await removeAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        Assert.Single(_log.Warnings, w => w.Contains($"{bea} added to team billing"));
        Assert.Single(_log.Warnings, w => w.Contains($"{bea} removed from team billing"));
        Assert.Empty(_log.Errors);
    }

    /// <summary>
    /// CR M2: adding waits for the account lock that disable takes, then re-reads the account, so a disable committed
    /// while the add waited is seen and nobody disabled ends up in a team. The disable is played by a transaction that
    /// holds that lock; a real race would be timing luck.
    /// </summary>
    [Fact]
    public async Task AddingAMember_WaitsForTheAccountLock_AndSeesADisableCommittedMeanwhile()
    {
        await TreeAsync();
        var (_, cal) = await _app.AddMemberAsync(_owner, "cal@skanyxx.example");
        await using var disabler = postgres.CreateDbContext();
        await using (var transaction = await disabler.Database.BeginTransactionAsync())
        {
            var email = await disabler.Users.Where(u => u.Id == cal).Select(u => u.NormalizedEmail!).SingleAsync();
            await AccountLock.AcquireAsync(disabler, email, CancellationToken.None);

            var add = Owner().PutAsync($"/api/identity/org/teams/billing/members/{cal}", null);
            await Task.Delay(500);
            Assert.False(add.IsCompleted, "the add did not wait for the account lock");

            await disabler.Users.Where(u => u.Id == cal).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, AccountStatus.DisabledUntil));
            await transaction.CommitAsync();

            Assert.Equal(HttpStatusCode.Conflict, (await add).StatusCode);
        }
        Assert.Empty((await MembershipAsync(cal)).Teams);
    }

    /// <summary>SEC L3 (accepted): disabling keeps the memberships — the person has no session to use them — and enabling brings them back.</summary>
    [Fact]
    public async Task ADisabledMember_KeepsTheMembership_AndHasItAgainOnceEnabled()
    {
        await TreeAsync();
        var (_, bea) = await _app.AddMemberAsync(_owner);
        await Owner().PutAsync($"/api/identity/org/teams/billing/members/{bea}", null);

        Assert.Equal(HttpStatusCode.OK, (await Owner().PostAsync($"/api/identity/people/{bea}/disable", null)).StatusCode);
        var whileDisabled = await MembershipAsync(bea);
        Assert.Equal(HttpStatusCode.OK, (await Owner().PostAsync($"/api/identity/people/{bea}/enable", null)).StatusCode);

        Assert.Equal(["billing"], whileDisabled.Teams);
        Assert.Equal(["billing"], (await MembershipAsync(bea)).Teams);
    }

    [Fact]
    public async Task EveryRoute_IsTheOwnersOnly()
    {
        await TreeAsync();
        var (member, memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        (HttpMethod Method, string Path, object? Body)[] routes =
        [
            (HttpMethod.Get, "/api/identity/org/departments", null),
            (HttpMethod.Post, "/api/identity/org/departments", new { slug = "x", name = "X" }),
            (HttpMethod.Put, "/api/identity/org/departments/finance", new { name = "X" }),
            (HttpMethod.Get, "/api/identity/org/teams", null),
            (HttpMethod.Post, "/api/identity/org/teams", new { slug = "x", name = "X", department = "finance" }),
            (HttpMethod.Put, "/api/identity/org/teams/billing", new { name = "X", department = "finance" }),
            (HttpMethod.Put, $"/api/identity/org/teams/billing/members/{memberId}", null),
            (HttpMethod.Delete, $"/api/identity/org/teams/billing/members/{memberId}", null),
            (HttpMethod.Get, $"/api/identity/people/{memberId}/teams", null)
        ];

        foreach (var (method, path, body) in routes)
        {
            var asSupervisor = await _app.Client(bearer: member.AccessToken).SendAsync(Request(method, path, body));
            var anonymous = await _app.Client().SendAsync(Request(method, path, body));
            Assert.True(asSupervisor.StatusCode == HttpStatusCode.Forbidden, $"{method} {path}: {asSupervisor.StatusCode}");
            Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path}: {anonymous.StatusCode}");
        }

        var teams = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/org/teams");
        Assert.Equal("""[{"slug":"billing","name":"Billing","department":"finance","members":[]}]""", teams.GetRawText());
        Assert.Equal(routes.Length, _log.Warnings.Count(w => w.Contains($"refused for {memberId}")));
        Assert.Contains(_log.Warnings, w => w.Contains($"Owner-only route PUT api/identity/org/teams/{{slug}}/members/{{userId}} refused for {memberId} from 127.0.0.1"));
    }

    [Fact]
    public async Task Changes_AreAuditedAtWarning_WithActorTargetAndAddress()
    {
        await TreeAsync();
        await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "ops", name = "Ops" });
        await Owner().PutAsJsonAsync("/api/identity/org/departments/ops", new { name = "Operations" });
        await Owner().PutAsJsonAsync("/api/identity/org/teams/billing", new { name = "Invoicing", department = "ops" });

        Assert.Contains($"Department finance created by {_ownerId} from 127.0.0.1", _log.Warnings);
        Assert.Contains($"Team billing created in department finance by {_ownerId} from 127.0.0.1", _log.Warnings);
        Assert.Contains($"Department ops renamed by {_ownerId} from 127.0.0.1", _log.Warnings);
        Assert.Contains($"Team billing renamed by {_ownerId} from 127.0.0.1", _log.Warnings);
        Assert.Contains($"Team billing moved from department finance to ops by {_ownerId} from 127.0.0.1", _log.Warnings);
    }

    /// <summary>The contract memory reads: live, departments derived from teams, a moved team moving its members.</summary>
    [Fact]
    public async Task Membership_IsLive_AndDepartmentsComeThroughTheTeams()
    {
        await TreeAsync();
        await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "ops", name = "Ops" });
        await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "tooling", name = "Tooling", department = "ops" });
        var (_, bea) = await _app.AddMemberAsync(_owner);
        var none = await MembershipAsync(bea);

        await Owner().PutAsync($"/api/identity/org/teams/billing/members/{bea}", null);
        var inBilling = await MembershipAsync(bea);
        await Owner().PutAsJsonAsync("/api/identity/org/teams/billing", new { name = "Billing", department = "ops" });
        var moved = await MembershipAsync(bea);
        await Owner().PutAsync($"/api/identity/org/teams/tooling/members/{bea}", null);
        await Owner().DeleteAsync($"/api/identity/org/teams/billing/members/{bea}");
        var afterRemoval = await MembershipAsync(bea);

        Assert.Empty(none.Teams);
        Assert.Empty(none.Departments);
        Assert.Equal(["billing"], inBilling.Teams);
        Assert.Equal(["finance"], inBilling.Departments);
        Assert.Equal(["ops"], moved.Departments);
        Assert.Equal(["tooling"], afterRemoval.Teams);
        Assert.Equal(["ops"], afterRemoval.Departments);
        Assert.Empty((await MembershipAsync(Guid.NewGuid().ToString())).Teams);
    }

    private HttpClient Owner() => _app.Client(bearer: _owner);

    private async Task TreeAsync()
    {
        Assert.Equal(HttpStatusCode.Created, (await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" })).StatusCode);
    }

    private async Task<OrgMembership> MembershipAsync(string userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrgMembership>().ForUserAsync(userId, CancellationToken.None);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body) =>
        new(method, path) { Content = body is null ? null : JsonContent.Create(body) };
}
