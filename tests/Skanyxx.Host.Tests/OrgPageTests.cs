using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>D055 in the browser: the owner's Org page (departments, teams, members), and teams on the People page.</summary>
[Collection(HostCollection.Name)]
public sealed partial class OrgPageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string MemberEmail = "bea@skanyxx.example";
    private const string MemberPassword = "a long member passphrase";

    private HostApp _host = null!;
    private Browser _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), HostApp.WithoutSandboxes);
        _owner = new Browser(_host);
        var setup = await _owner.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Owner_BuildsTheTreeOnThePage_AndPeopleShowsTheTeams()
    {
        var memberId = await MemberAsync();

        var department = await _owner.SubmitAsync("/Org?handler=CreateDepartment", new() { ["slug"] = "finance", ["name"] = "Finance" }, tokenFrom: "/Org");
        var second = await _owner.SubmitAsync("/Org?handler=CreateDepartment", new() { ["slug"] = "ops", ["name"] = "Ops" }, tokenFrom: "/Org");
        var team = await _owner.SubmitAsync("/Org?handler=CreateTeam", new() { ["slug"] = "billing", ["name"] = "Billing", ["department"] = "finance" }, tokenFrom: "/Org");
        var added = await _owner.SubmitAsync("/Org?handler=AddMember&slug=billing", new() { ["userId"] = memberId }, tokenFrom: "/Org");
        var moved = await _owner.SubmitAsync("/Org?handler=UpdateTeam&slug=billing", new() { ["name"] = "Invoicing", ["department"] = "ops" }, tokenFrom: "/Org");
        var renamed = await _owner.SubmitAsync("/Org?handler=RenameDepartment&slug=finance", new() { ["name"] = "Money" }, tokenFrom: "/Org");
        var taken = await _owner.SubmitAsync("/Org?handler=CreateTeam", new() { ["slug"] = "billing", ["name"] = "Again", ["department"] = "ops" }, tokenFrom: "/Org");
        var bad = await _owner.SubmitAsync("/Org?handler=CreateDepartment", new() { ["slug"] = "Bad Slug", ["name"] = "Bad" }, tokenFrom: "/Org");
        var unknown = await _owner.SubmitAsync("/Org?handler=AddMember&slug=nope", new() { ["userId"] = memberId }, tokenFrom: "/Org");
        var page = await (await _owner.GetAsync("/Org")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await _owner.SubmitAsync($"/People?handler=Disable&id={memberId}", [], tokenFrom: "/People");
        var pageWhileDisabled = await (await _owner.GetAsync("/Org")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var people = await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var removed = await _owner.SubmitAsync($"/Org?handler=RemoveMember&slug=billing&userId={memberId}", [], tokenFrom: "/Org");
        var teams = await _host.Client(await BearerAsync(HostApp.OwnerEmail, HostApp.OwnerPassword)).GetFromJsonAsync<JsonElement>("/api/identity/org/teams", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Department created.", await department.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Department created.", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Team created.", await team.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Member added.", await added.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Team saved.", await moved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Department renamed.", await renamed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("already exists", await taken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode); // CR M4: the status says what the page says
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("No team", await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(">disabled</span>", page);
        Assert.Contains(">disabled</span>", pageWhileDisabled); // SEC L3: kept, and marked
        Assert.Contains("department:finance", page);
        Assert.Contains("team:billing", page);
        Assert.Contains("value=\"Invoicing\"", page);
        Assert.Contains("value=\"Money\"", page);
        Assert.Contains("Bea", page);
        Assert.Contains("<td>Invoicing</td>", people);
        Assert.Contains("Member removed.", await removed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("""[{"slug":"billing","name":"Invoicing","department":"ops","members":[]}]""", teams.GetRawText());
    }

    [Fact]
    public async Task Org_IsTheOwnersOnly_AndOnlyTheOwnerSeesTheNavLink()
    {
        await MemberAsync(role: "supervisor");
        var member = new Browser(_host);
        await member.SubmitAsync("/Login", new() { ["Email"] = MemberEmail, ["Password"] = MemberPassword });

        var ownerNav = await (await _owner.GetAsync("/Hooks")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var memberNav = await (await member.GetAsync("/Hooks")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var memberPage = await member.GetAsync("/Org");
        var memberPost = await member.SubmitAsync("/Org?handler=CreateDepartment", new() { ["slug"] = "evil", ["name"] = "Evil" }, tokenFrom: "/Hooks");
        var anonymous = await _host.Client().GetAsync("/Org", TestContext.Current.CancellationToken);

        Assert.Contains("href=\"/Org\"", ownerNav);
        Assert.DoesNotContain("href=\"/Org\"", memberNav);
        Assert.Equal(HttpStatusCode.Redirect, memberPage.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, memberPost.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.DoesNotContain("department:evil", await (await _owner.GetAsync("/Org")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OrgPost_NeedsAntiforgery_AndTheOwnOrigin()
    {
        var html = await (await _owner.GetAsync("/Org")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var antiforgery = Antiforgery().Match(html).Groups[1].Value;

        var noToken = await _owner.PostFormAsync("/Org?handler=CreateDepartment", new() { ["slug"] = "a", ["name"] = "A" });
        var foreign = await _owner.PostFormAsync("/Org?handler=CreateDepartment",
            new() { ["slug"] = "b", ["name"] = "B", ["__RequestVerificationToken"] = antiforgery },
            r => r.Headers.Add("Origin", "https://evil.example"));
        var page = await (await _owner.GetAsync("/Org")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.DoesNotContain("department:a<", page);
        Assert.DoesNotContain("department:b<", page);
    }

    private async Task<string> MemberAsync(string role = "employee")
    {
        var invited = await _owner.SubmitAsync("/People?handler=Invite", new() { ["email"] = MemberEmail, ["roles"] = role }, tokenFrom: "/People");
        var token = InviteToken().Match(await invited.Content.ReadAsStringAsync()).Groups[1].Value;
        var member = new Browser(_host);
        var accepted = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword, ["DisplayName"] = "Bea" },
            tokenFrom: "/Invite?token=" + token);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        var people = await _host.Client(await BearerAsync(HostApp.OwnerEmail, HostApp.OwnerPassword)).GetFromJsonAsync<JsonElement>("/api/identity/people");
        return people.EnumerateArray().Single(p => p.GetProperty("email").GetString() == MemberEmail).GetProperty("id").GetString()!;
    }

    private async Task<string> BearerAsync(string email, string password)
    {
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password });
        signIn.EnsureSuccessStatusCode();
        return (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    [GeneratedRegex("/Invite\\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex InviteToken();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();
}
