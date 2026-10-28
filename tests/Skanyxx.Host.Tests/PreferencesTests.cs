using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Per-person appearance and layout (<c>api/me/preferences</c>, the Account page, the layout): only the caller's own,
/// only allow-listed values, inherited from the organization's defaults, applied server-side on the first render, and a
/// saved nav order, pin or landing page never shows a panel the person's roles do not allow.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class PreferencesTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task NewPerson_GetsTheDefaults()
    {
        var (_, api) = await MemberAsync("ann@skanyxx.example", "employee");

        var view = await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);

        Assert.DoesNotContain(view.GetProperty("saved").EnumerateObject(), p => p.Value.ValueKind != JsonValueKind.Null);
        var effective = view.GetProperty("effective");
        Assert.Equal("system", effective.GetProperty("theme").GetString());
        Assert.Equal("auto", effective.GetProperty("accent").GetString());
        Assert.Equal("comfortable", effective.GetProperty("density").GetString());
        Assert.Equal("m", effective.GetProperty("fontSize").GetString());
        Assert.Equal(25, effective.GetProperty("tablePageSize").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/me/preferences", Ct)).StatusCode);
    }

    [Fact]
    public async Task Saved_Persists_AcrossSignIns_AndTheLayoutRendersIt_OnTheFirstPaint()
    {
        var (browser, api) = await MemberAsync("ann@skanyxx.example", "employee");

        var save = await api.PutAsJsonAsync("/api/me/preferences", new
        {
            theme = "teal", accent = "rose", density = "compact", fontSize = "l", sidebar = "collapsed",
            chatDetails = "hidden", tablePageSize = 50, motion = "reduce"
        }, Ct);
        var again = _host.Client(await BearerAsync("ann@skanyxx.example"));
        var view = await again.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var signedInAgain = new Browser(_host);
        await signedInAgain.SubmitAsync("/Login", new() { ["Email"] = "ann@skanyxx.example", ["Password"] = Password });
        var html = await (await signedInAgain.GetAsync("/Library")).Content.ReadAsStringAsync(Ct);
        var root = HtmlTag().Match(html).Value;

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("teal", view.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Equal(50, view.GetProperty("saved").GetProperty("tablePageSize").GetInt32());
        Assert.Contains("data-theme=\"teal\"", root);
        Assert.Contains("data-accent=\"rose\"", root);
        Assert.Contains("data-density=\"compact\"", root);
        Assert.Contains("data-font-size=\"l\"", root);
        Assert.Contains("data-sidebar=\"collapsed\"", root);
        Assert.Contains("data-motion=\"reduce\"", root);
        Assert.Contains("data-chat-details=\"hidden\"", root);
        Assert.Contains("data-page-size=\"50\"", root);
        Assert.Contains("data-theme=\"teal\"", HtmlTag().Match(await (await browser.GetAsync("/Library")).Content.ReadAsStringAsync(Ct)).Value);
    }

    [Fact]
    public async Task EachPerson_ReadsAndWritesOnlyTheirOwn()
    {
        var (_, ann) = await MemberAsync("ann@skanyxx.example", "employee");
        var (_, bob) = await MemberAsync("bob@skanyxx.example", "employee");
        var annId = (await ann.GetFromJsonAsync<JsonElement>("/api/identity/me", Ct)).GetProperty("id").GetString();

        await ann.PutAsJsonAsync("/api/me/preferences", new { theme = "rose" }, Ct);
        var bobView = await bob.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var smuggled = await bob.PutAsJsonAsync("/api/me/preferences", new { userId = annId, theme = "orange" }, Ct);
        var orgRead = await bob.GetAsync("/api/identity/org/preferences", Ct);
        var orgWrite = await bob.PutAsJsonAsync("/api/identity/org/preferences", new { theme = "orange" }, Ct);
        var annView = await ann.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);

        Assert.Equal("system", bobView.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, smuggled.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, orgRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, orgWrite.StatusCode);
        Assert.Equal("rose", annView.GetProperty("effective").GetProperty("theme").GetString());
    }

    public static TheoryData<string> Refused() =>
    [
        """{"theme":"neon"}""",
        """{"theme":"dark\"><script>alert(1)</script>"}""",
        """{"theme":"dark; } body { background: url(https://evil.example/x) }"}""",
        """{"accent":"#ff0000"}""",
        """{"density":"COMPACT"}""",
        """{"css":"body{display:none}"}""",
        """{"backgroundUrl":"https://evil.example/x.png"}""",
        """{"tablePageSize":7}""",
        """{"tablePageSize":"25px"}""",
        """{"navOrder":["chat","javascript:alert(1)"]}""",
        """{"navOrder":["chat","chat"]}""",
        """{"pinnedNav":["chat","library","agents","studio","plugins","sandboxes","models"]}""",
        """{"landing":"https://evil.example"}""",
        """{"motion":"off"}"""
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task OnlyAllowListedKeysAndValues_AreAccepted(string body)
    {
        var (_, api) = await MemberAsync("ann@skanyxx.example", "employee");
        await api.PutAsJsonAsync("/api/me/preferences", new { theme = "green" }, Ct);

        var response = await api.PutAsync("/api/me/preferences", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);
        var view = await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var saved = view.GetProperty("saved");
        Assert.Equal("green", saved.GetProperty("theme").GetString());
        Assert.All(saved.EnumerateObject().Where(p => p.Name != "theme"), p => Assert.Equal(JsonValueKind.Null, p.Value.ValueKind));
    }

    [Fact]
    public async Task Reset_DropsTheOwnChoices_AndOrganizationDefaultsAreInherited()
    {
        var (_, ann) = await MemberAsync("ann@skanyxx.example", "employee");
        var (_, bob) = await MemberAsync("bob@skanyxx.example", "employee");
        var owner = _host.Client(await BearerAsync(HostApp.OwnerEmail, HostApp.OwnerPassword));

        var org = await owner.PutAsJsonAsync("/api/identity/org/preferences", new { theme = "green", density = "compact" }, Ct);
        await ann.PutAsJsonAsync("/api/me/preferences", new { theme = "purple" }, Ct);
        var annOwn = await ann.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var bobInherits = await bob.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var reset = await ann.DeleteAsync("/api/me/preferences", Ct);
        var annAfter = await ann.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var audit = await owner.GetFromJsonAsync<JsonElement>("/api/identity/audit?action=org.preferences_saved", Ct);

        Assert.Equal(HttpStatusCode.OK, org.StatusCode);
        Assert.Equal("purple", annOwn.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Equal("compact", annOwn.GetProperty("effective").GetProperty("density").GetString());
        Assert.Equal("green", bobInherits.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("green", annAfter.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Equal(1, audit.GetArrayLength());
    }

    [Fact]
    public async Task NavOrderPinsAndLanding_NeverRevealAPanelTheRoleCannotSee()
    {
        var (browser, api) = await MemberAsync("ann@skanyxx.example", "employee");

        var save = await api.PutAsJsonAsync("/api/me/preferences", new
        {
            pinnedNav = new[] { "people", "audit", "debug", "library" },
            navOrder = new[] { "debug", "people", "models", "agents", "chat" },
            landing = "people"
        }, Ct);
        var html = await (await browser.GetAsync("/Library")).Content.ReadAsStringAsync(Ct);
        var home = await browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, save.StatusCode); // valid keys; the nav decides what the role sees
        foreach (var panel in new[] { "/People", "/Audit", "/Debug", "/Model", "/Studio", "/Sandboxes", "/ToolServers" })
            Assert.DoesNotContain($"href=\"{panel}\"", html);
        Assert.Contains("id=\"nav-pinned\"", html);
        Assert.True(html.IndexOf("href=\"/Agents\"", StringComparison.Ordinal) < html.IndexOf("href=\"/Chat\"", StringComparison.Ordinal),
            "the saved order puts Agents before Chat");
        Assert.True(html.IndexOf("href=\"/Library\"", StringComparison.Ordinal) < html.IndexOf("href=\"/Agents\"", StringComparison.Ordinal),
            "the pinned Library comes first");
        Assert.Equal("/Chat", home.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Landing_OpensTheChosenPanel()
    {
        var (browser, api) = await MemberAsync("ann@skanyxx.example", "employee");

        await api.PutAsJsonAsync("/api/me/preferences", new { landing = "library" }, Ct);

        Assert.Equal("/Library", (await browser.GetAsync("/")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AccountPage_SavesAndResets_ThroughTheForm()
    {
        var (browser, api) = await MemberAsync("ann@skanyxx.example", "employee");

        var page = await (await browser.GetAsync("/Account")).Content.ReadAsStringAsync(Ct);
        var saved = await browser.SubmitAsync("/Account?handler=Appearance", new()
        {
            ["Appearance.Theme"] = "orange", ["Appearance.Accent"] = "teal", ["Appearance.Density"] = "compact",
            ["Appearance.FontSize"] = "s", ["Appearance.Sidebar"] = "expanded", ["Appearance.Motion"] = "system",
            ["Appearance.ChatDetails"] = "shown", ["Appearance.Landing"] = "library", ["Appearance.TablePageSize"] = "10",
            ["Appearance.NavOrder"] = "library,chat,agents", ["Appearance.PinnedNav"] = "agents"
        }, tokenFrom: "/Account");
        var savedHtml = await saved.Content.ReadAsStringAsync(Ct);
        var view = await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var refused = await browser.SubmitAsync("/Account?handler=Appearance", new() { ["Appearance.Theme"] = "<script>" }, tokenFrom: "/Account");
        var reset = await browser.SubmitAsync("/Account?handler=AppearanceReset", [], tokenFrom: "/Account");
        var afterReset = await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);
        var orgByMember = await browser.SubmitAsync("/Account?handler=OrganizationDefaults", new() { ["Appearance.Theme"] = "rose" }, tokenFrom: "/Account");

        Assert.Contains("Appearance &amp; layout", page);
        Assert.DoesNotContain("Save as organization default", page);
        Assert.DoesNotContain("value=\"people\"", page); // only panels the role can open are offered
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("Appearance saved.", savedHtml);
        Assert.Contains("data-theme=\"orange\"", HtmlTag().Match(savedHtml).Value);
        Assert.Equal("orange", view.GetProperty("saved").GetProperty("theme").GetString());
        Assert.Equal(["library", "chat", "agents"], view.GetProperty("saved").GetProperty("navOrder").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.DoesNotContain("<script>", HtmlTag().Match(await refused.Content.ReadAsStringAsync(Ct)).Value);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("system", afterReset.GetProperty("effective").GetProperty("theme").GetString());
        Assert.Contains("/AccessDenied", orgByMember.Headers.Location?.OriginalString); // the page's 403
        Assert.Equal("system", (await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct)).GetProperty("organizationDefaults").GetProperty("theme").GetString() ?? "system");
    }

    [Fact]
    public async Task Owner_SavesOrganizationDefaults_FromTheAccountPage()
    {
        var page = await (await _owner.GetAsync("/Account")).Content.ReadAsStringAsync(Ct);
        var saved = await _owner.SubmitAsync("/Account?handler=OrganizationDefaults", new() { ["Appearance.Theme"] = "blue" }, tokenFrom: "/Account");
        var (_, member) = await MemberAsync("ann@skanyxx.example", "employee");

        Assert.Contains("Save as organization default", page);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("blue", (await member.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct)).GetProperty("effective").GetProperty("theme").GetString());
    }

    private async Task<(Browser Browser, HttpClient Api)> MemberAsync(string email, string role)
    {
        var invited = await _owner.SubmitAsync("/People?handler=Invite", new() { ["email"] = email, ["roles"] = role }, tokenFrom: "/People");
        var token = InviteToken().Match(await invited.Content.ReadAsStringAsync(Ct)).Groups[1].Value;
        var member = new Browser(_host);
        var accepted = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = Password }, tokenFrom: "/Invite?token=" + token);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        return (member, _host.Client(await BearerAsync(email)));
    }

    private async Task<string> BearerAsync(string email, string password = Password)
    {
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password }, Ct);
        signIn.EnsureSuccessStatusCode();
        return (await signIn.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    [GeneratedRegex("/Invite\\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex InviteToken();

    [GeneratedRegex("<html[^>]*>")]
    private static partial Regex HtmlTag();
}
