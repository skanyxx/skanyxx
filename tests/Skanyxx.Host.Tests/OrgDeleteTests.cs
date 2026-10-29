using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D351: the owner deletes teams and departments. One with members (teams) or teams (departments) is refused unless
/// the owner names where they go; a deleted slug is never reused; every delete is audited; nobody else may.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class OrgDeleteTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HostApp _host = null!;
    private string _database = null!;
    private HttpClient _owner = null!;
    private string _beaId = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(_database, HostApp.WithoutSandboxes);
        _owner = await _host.OwnerAsync();
        (_, _beaId) = await TestPeople.InviteAsync(_host, _owner, "bea@skanyxx.example", "employee");
        Created(await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }, Ct));
        Created(await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "ops", name = "Ops" }, Ct));
        Created(await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }, Ct));
        Created(await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "payroll", name = "Payroll", department = "finance" }, Ct));
        (await _owner.PutAsync($"/api/identity/org/teams/billing/members/{_beaId}", null, Ct)).EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task ATeamWithMembers_IsRefused_UntilTheyHaveSomewhereToGo()
    {
        var refused = await _owner.DeleteAsync("/api/identity/org/teams/billing", Ct);
        var toItself = await _owner.DeleteAsync("/api/identity/org/teams/billing?to=billing", Ct);
        var toNowhere = await _owner.DeleteAsync("/api/identity/org/teams/billing?to=nope", Ct);
        var stillThere = await TeamsAsync();
        var reassigned = await _owner.DeleteAsync("/api/identity/org/teams/billing?to=payroll", Ct);
        var after = await TeamsAsync();
        var reused = await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing again", department = "ops" }, Ct);
        var missing = await _owner.DeleteAsync("/api/identity/org/teams/billing", Ct);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 member(s)", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, toItself.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, toNowhere.StatusCode);
        Assert.Equal([_beaId], stillThere["billing"]);
        Assert.Equal(HttpStatusCode.NoContent, reassigned.StatusCode);
        Assert.False(after.ContainsKey("billing"));
        Assert.Equal([_beaId], after["payroll"]);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Contains("never reused", await reused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var audit = Assert.Single(await TestPeople.AuditAsync(_database, "org.team_deleted"));
        Assert.Equal("billing", audit.Target);
        Assert.Contains("\"reassignedTo\": \"payroll\"", audit.Details);
        Assert.Contains("\"members\": 1", audit.Details);
    }

    [Fact]
    public async Task AnEmptyTeam_IsDeleted_AndADepartmentWithTeams_MovesThemFirst()
    {
        var emptyTeam = await _owner.DeleteAsync("/api/identity/org/teams/payroll", Ct);
        var refused = await _owner.DeleteAsync("/api/identity/org/departments/finance", Ct);
        var moved = await _owner.DeleteAsync("/api/identity/org/departments/finance?to=ops", Ct);
        var teams = await _owner.GetFromJsonAsync<JsonElement>("/api/identity/org/teams", Ct);
        var departments = await _owner.GetFromJsonAsync<JsonElement>("/api/identity/org/departments", Ct);
        var reused = await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }, Ct);
        var emptyDepartment = await _owner.DeleteAsync("/api/identity/org/departments/ops?to=", Ct);

        Assert.Equal(HttpStatusCode.NoContent, emptyTeam.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal("ops", teams.EnumerateArray().Single().GetProperty("department").GetString());
        Assert.Equal([_beaId], teams.EnumerateArray().Single().GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["ops"], departments.EnumerateArray().Select(d => d.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, emptyDepartment.StatusCode); // ops now holds billing
        Assert.Single(await TestPeople.AuditAsync(_database, "org.department_deleted"));
        Assert.Contains(await TestPeople.AuditAsync(_database, "org.team_moved"), a => a.Target == "billing" && a.Details.Contains("\"to\": \"ops\""));
    }

    [Fact]
    public async Task OnThePage_DeleteAsksWhereMembersGo_AndRefusesWithout()
    {
        var owner = await TestPeople.SignInAsync(_host, HostApp.OwnerEmail, HostApp.OwnerPassword);

        var page = await owner.HtmlAsync("/Org");
        var refused = await owner.SubmitAsync("/Org?handler=DeleteTeam&slug=billing", [], tokenFrom: "/Org");
        var deleted = await owner.SubmitAsync("/Org?handler=DeleteTeam&slug=billing", new() { ["to"] = "payroll" }, tokenFrom: "/Org");
        var renamedTeam = await owner.SubmitAsync("/Org?handler=UpdateTeam&slug=payroll", new() { ["name"] = "Pay", ["department"] = "ops" }, tokenFrom: "/Org");

        Assert.Contains("data-confirm-title=\"Delete Billing?\"", page);
        Assert.Contains("name=\"to\"", page);
        Assert.DoesNotContain("id=\"setup-org\"", page); // set up: the panel shows no guide
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 member(s)", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Team deleted.", await deleted.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Team saved.", await renamedTeam.Content.ReadAsStringAsync(Ct));
        Assert.Equal([_beaId], (await TeamsAsync())["payroll"]);
    }

    [Fact]
    public async Task OnlyTheOwner_MayDelete()
    {
        foreach (var role in new[] { "employee", "builder", "supervisor" })
        {
            var email = $"{role}-x@skanyxx.example";
            var (api, _) = await TestPeople.InviteAsync(_host, _owner, email, role);
            var browser = await TestPeople.SignInAsync(_host, email);

            Assert.Equal(HttpStatusCode.Forbidden, (await api.DeleteAsync("/api/identity/org/teams/payroll", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.DeleteAsync("/api/identity/org/departments/ops", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect,
                (await browser.SubmitAsync("/Org?handler=DeleteTeam&slug=payroll", [], tokenFrom: "/Library")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().DeleteAsync("/api/identity/org/teams/payroll", Ct)).StatusCode);
        Assert.True((await TeamsAsync()).ContainsKey("payroll"));
        Assert.Empty(await TestPeople.AuditAsync(_database, "org.team_deleted"));
    }

    private async Task<Dictionary<string, string[]>> TeamsAsync() =>
        (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/org/teams", Ct)).EnumerateArray().ToDictionary(
            t => t.GetProperty("slug").GetString()!, t => t.GetProperty("members").EnumerateArray().Select(m => m.GetString()!).ToArray());

    private static void Created(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}
