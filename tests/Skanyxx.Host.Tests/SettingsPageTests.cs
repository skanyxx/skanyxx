using System.Net.Http.Json;
using System.Text.Json;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D350/D352/D354: the owner's installation settings. Owner only on the page and the API; Microsoft sign-in off by
/// default and turned on without a restart; every change audited with key and old/new values (never a secret's);
/// Cloud tools locked without the licence; per-role landing pages.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class SettingsPageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HostApp _host = null!;
    private string _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(_database, HostApp.WithoutSandboxes);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Settings_IsTheOwnersOnly_OnThePageAndTheApi()
    {
        var owner = await _host.OwnerAsync();
        foreach (var role in new[] { "employee", "builder", "supervisor" })
        {
            var email = $"{role}@skanyxx.example";
            var (api, _) = await TestPeople.InviteAsync(_host, owner, email, role);
            var browser = await TestPeople.SignInAsync(_host, email);

            var page = await browser.GetAsync("/Settings");
            var post = await browser.SubmitAsync("/Settings", new() { ["section"] = "General", ["general.installationName"] = "Evil" }, tokenFrom: "/Library");
            var get = await api.GetAsync("/api/identity/settings", Ct);
            var put = await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["general.installationName"] = "Evil" } }, Ct);
            var nav = await browser.HtmlAsync("/Library");

            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, page.Headers.Location!).AbsolutePath);
            Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
            Assert.DoesNotContain("href=\"/Settings\"", nav);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/identity/settings", Ct)).StatusCode);

        var values = await owner.GetFromJsonAsync<JsonElement>("/api/identity/settings", Ct);
        Assert.Equal("", values.GetProperty("general.installationName").GetString()); // nobody changed it
        Assert.Empty(await TestPeople.AuditAsync(_database, "settings.changed"));
    }

    [Fact]
    public async Task MicrosoftSignIn_IsOffByDefault_AndTurnsOnAndOffWithoutARestart()
    {
        var api = await _host.OwnerAsync();
        var owner = await TestPeople.OwnerBrowserAsync(_host);

        var before = await api.GetFromJsonAsync<JsonElement>("/api/identity/settings", Ct);
        var settingsPage = await owner.HtmlAsync("/Settings");
        var entraBefore = await owner.GetAsync("/Entra");
        var apiBefore = await api.GetAsync("/api/identity/entra/settings", Ct);

        var on = await owner.SubmitAsync("/Settings", new() { ["section"] = "Sign-in", ["signin.entra"] = "true" });
        var entraOn = await owner.GetAsync("/Entra");
        var apiOn = await api.GetAsync("/api/identity/entra/settings", Ct);
        var navOn = await owner.HtmlAsync("/Library");

        var off = await owner.SubmitAsync("/Settings", new() { ["section"] = "Sign-in" }); // an unticked box posts nothing
        var entraOff = await owner.GetAsync("/Entra");

        Assert.Equal("false", before.GetProperty("signin.entra").GetString());
        Assert.Contains("name=\"signin.entra\"", settingsPage);
        Assert.Equal(HttpStatusCode.NotFound, entraBefore.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, apiBefore.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, on.StatusCode);
        Assert.Equal(HttpStatusCode.OK, entraOn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apiOn.StatusCode);
        Assert.Contains("href=\"/Entra\"", navOn);
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, entraOff.StatusCode);
        var audit = await TestPeople.AuditAsync(_database, "settings.changed");
        Assert.Equal(2, audit.Count);
        Assert.Equal("signin.entra", audit[0].Target);
        Assert.Contains("\"old\": \"false\"", audit[0].Details);
        Assert.Contains("\"new\": \"true\"", audit[0].Details);
    }

    [Fact]
    public async Task EveryChange_IsAuditedWithKeyAndOldAndNew_OnlyWhenItChanged_AndAppliesAtOnce()
    {
        var api = await _host.OwnerAsync();
        var owner = await TestPeople.OwnerBrowserAsync(_host);
        var general = new Dictionary<string, string>
        {
            ["section"] = "General", ["general.installationName"] = "Acme Agents", ["general.defaultTheme"] = "teal",
            ["general.landing.owner"] = "dashboard", ["general.landing.supervisor"] = "dashboard",
            ["general.landing.builder"] = "auto", ["general.landing.employee"] = "library"
        };

        var saved = await owner.SubmitAsync("/Settings", general);
        var again = await owner.SubmitAsync("/Settings", general);
        var page = await owner.HtmlAsync("/Settings");
        var retention = await api.PutAsJsonAsync("/api/identity/settings",
            new { values = new Dictionary<string, string> { ["retention.auditDays"] = "90", ["retention.chatDays"] = "30" } }, Ct);
        var outOfRange = await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["retention.auditDays"] = "0" } }, Ct);
        var unknown = await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["nope"] = "1" } }, Ct);
        var values = await api.GetFromJsonAsync<JsonElement>("/api/identity/settings", Ct);
        var preferences = await api.GetFromJsonAsync<JsonElement>("/api/me/preferences", Ct);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("<title>Settings - Acme Agents</title>", page); // the layout reads it at once
        Assert.Contains("Acme Agents</div>", page);
        Assert.Equal(HttpStatusCode.OK, retention.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("90", values.GetProperty("retention.auditDays").GetString());
        Assert.Equal("teal", values.GetProperty("general.defaultTheme").GetString());
        Assert.Equal("teal", preferences.GetProperty("effective").GetProperty("theme").GetString()); // the organization's default

        var audit = await TestPeople.AuditAsync(_database, "settings.changed");
        // Name, theme and the employee's landing changed; the landings left at their defaults and the repeat wrote nothing.
        Assert.Equal(["general.defaultTheme", "general.installationName", "general.landing.employee", "retention.auditDays", "retention.chatDays"],
            audit.Select(a => a.Target!).Order());
        var name = JsonDocument.Parse(audit.Single(a => a.Target == "general.installationName").Details).RootElement;
        Assert.Equal("general.installationName", name.GetProperty("key").GetString());
        Assert.Equal("", name.GetProperty("old").GetString());
        Assert.Equal("Acme Agents", name.GetProperty("new").GetString());
    }

    [Fact]
    public async Task AnEmployee_LandsWhereTheSettingsSay_AndAPersonalChoiceWins()
    {
        var api = await _host.OwnerAsync();
        var (employeeApi, _) = await TestPeople.InviteAsync(_host, api, "emma@skanyxx.example", "employee");
        var emma = await TestPeople.SignInAsync(_host, "emma@skanyxx.example");

        var defaultHome = (await emma.GetAsync("/")).Headers.Location?.OriginalString;
        (await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["general.landing.employee"] = "library" } }, Ct)).EnsureSuccessStatusCode();
        var settingHome = (await emma.GetAsync("/")).Headers.Location?.OriginalString;
        (await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["general.landing.employee"] = "people" } }, Ct)).EnsureSuccessStatusCode();
        var forbiddenHome = (await emma.GetAsync("/")).Headers.Location?.OriginalString;
        (await employeeApi.PutAsJsonAsync("/api/me/preferences", new { landing = "agents" }, Ct)).EnsureSuccessStatusCode();
        var ownHome = (await emma.GetAsync("/")).Headers.Location?.OriginalString;

        Assert.Equal("/Chat", defaultHome);
        Assert.Equal("/Library", settingHome);
        Assert.Equal("/Chat", forbiddenHome); // a panel the role cannot open is never a landing
        Assert.Equal("/Agents", ownHome);
    }

    [Fact]
    public async Task CloudTools_IsLockedWithoutTheLicence()
    {
        var api = await _host.OwnerAsync();
        var refused = await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["features.cloudTools"] = "true" } }, Ct);
        var page = await (await TestPeople.OwnerBrowserAsync(_host)).HtmlAsync("/Settings");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("paid feature", page);
        Assert.Matches("name=\"features.cloudTools\"[^>]*disabled", page);
        Assert.Empty(await TestPeople.AuditAsync(_database, "settings.changed"));
    }

    [Fact]
    public async Task CloudTools_TurnsOnWithTheLicence()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            HostApp.WithoutSandboxes(s);
            s[CloudToolsFeature.LicenceKey] = "true";
        });
        var api = await host.OwnerAsync();

        var on = await api.PutAsJsonAsync("/api/identity/settings", new { values = new Dictionary<string, string> { ["features.cloudTools"] = "true" } }, Ct);
        var values = await api.GetFromJsonAsync<JsonElement>("/api/identity/settings", Ct);

        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal("true", values.GetProperty("features.cloudTools").GetString());
    }

    [Fact]
    public void ASecretSetting_IsAuditedAsChanged_WithoutItsValues()
    {
        var secret = new InstallationSetting("x.secret", "General", "Secret", "", SettingKind.Text, "", "X:Secret", "now", MaxLength: 100, Secret: true);
        var plain = InstallationSettings.Find(InstallationSettings.InstallationName)!;

        var secretDetails = JsonSerializer.Serialize(InstallationSettings.AuditDetails(secret, "old-value-123", "new-value-456"));
        var plainDetails = JsonSerializer.Serialize(InstallationSettings.AuditDetails(plain, "A", "B"));

        Assert.Equal("""{"key":"x.secret","changed":true}""", secretDetails);
        Assert.Equal("""{"key":"general.installationName","old":"A","new":"B"}""", plainDetails);
    }

    [Fact]
    public void EveryCatalogDefault_PassesItsOwnRule_AndKeysAreUnique()
    {
        Assert.Equal(InstallationSettings.All.Count, InstallationSettings.All.Select(s => s.Key).Distinct().Count());
        Assert.All(InstallationSettings.All, s => Assert.Null(InstallationSettings.Problem(s, s.Default)));
        Assert.Equal("false", InstallationSettings.Find(InstallationSettings.Entra)!.Default);
        Assert.Equal("dashboard", InstallationSettings.Find(InstallationSettings.LandingKey("owner"))!.Default);
    }
}
