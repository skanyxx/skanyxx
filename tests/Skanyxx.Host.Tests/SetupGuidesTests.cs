using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Skanyxx.Host.Setup;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Setup guides: who may read them (owner and supervisors; everyone else gets "ask your admin"), what they detect as
/// missing (names only), and that no configured value, secrets above all, ever reaches a page, the API or the
/// "Copy instructions for agent" text.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class SetupGuidesTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";

    // Fake secrets: each must never appear anywhere a guide is shown.
    private const string GitToken = "fake-git-token-5f2c9e1a7b3d";
    private const string SmtpPassword = "fake-smtp-password-81d4c0";
    private const string DragonflyPassword = "fake-dragonfly-password-3e7a";
    private const string SandboxKey = "fake-sandbox-token-key-of-at-least-32-chars-9c1b";

    private static readonly string[] Secrets = [GitToken, SmtpPassword, DragonflyPassword, SandboxKey];

    private string _database = null!;

    public async ValueTask InitializeAsync() => _database = await fixture.NewDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Inspector_NamesWhatIsMissing_AndReadsSwitchesListsAndAlternatives()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Studio:Git:BaseUrl"] = "http://git.example",
            ["Sandboxes:Enabled"] = "false",
            ["Sandboxes:AllowedImages:0"] = "ghcr.io/acme/",
            ["KAgent:IngressUrl"] = "http://kagent.example:8083",
        }).Build();
        var inspector = new SetupInspector(configuration);

        var studio = inspector.Inspect(SetupGuides.Find("studio")!);
        var sandboxes = inspector.Inspect(SetupGuides.Find("sandboxes")!);
        var chat = inspector.Inspect(SetupGuides.Find("chat")!);

        Assert.False(studio.Ready);
        Assert.Equal(["Studio:Git:Token"], studio.Missing.Select(s => s.Key));
        Assert.Contains("Studio:MemoryMcpUrl", studio.OptionalUnset.Select(s => s.Key));
        Assert.Contains("Studio:Git:BaseUrl", studio.Set.Select(s => s.Key));
        // "false" is not on; a list counts by its children.
        Assert.Contains("Sandboxes:Enabled", sandboxes.Missing.Select(s => s.Key));
        Assert.DoesNotContain("Sandboxes:AllowedImages", sandboxes.Missing.Select(s => s.Key));
        // KAgent:IngressUrl stands in for KAgent:BaseUrl.
        Assert.DoesNotContain("KAgent:BaseUrl", chat.Missing.Select(s => s.Key));
        Assert.Contains("Identity:PublicBaseUrl", chat.Missing.Select(s => s.Key));
    }

    [Fact]
    public void EveryGuide_IsComplete_AndItsAgentPromptNamesTheMissingSettings()
    {
        var empty = new SetupInspector(new ConfigurationBuilder().Build());
        Assert.Equal(SetupGuides.All.Count, SetupGuides.All.Select(g => g.Id).Distinct().Count());
        foreach (var guide in SetupGuides.All)
        {
            Assert.NotEmpty(guide.Prerequisites);
            Assert.NotEmpty(guide.Settings);
            Assert.NotEmpty(guide.Local);
            Assert.NotEmpty(guide.Helm);
            Assert.NotEmpty(guide.Verify);
            Assert.NotEmpty(guide.Errors);

            var state = empty.Inspect(guide);
            var prompt = AgentPrompt.For(guide, state);
            Assert.Contains(guide.Title, prompt);
            Assert.Contains("Verify:", prompt);
            Assert.All(state.Missing, s => Assert.Contains($"NOT WORKING: {s.Name} is not set.", prompt));
        }
    }

    [Fact]
    public async Task Owner_GetsGuidesWithMissingNames_AndNoConfiguredValueAnywhere()
    {
        await using var host = await HostApp.StartAsync(_database, s =>
        {
            s["Studio:Git:Token"] = GitToken; // BaseUrl left unset: the studio is missing exactly that.
            s["Identity:Smtp:Host"] = "smtp.example.invalid";
            s["Identity:Smtp:From"] = "skanyxx@example.invalid";
            s["Identity:Smtp:UserName"] = "skanyxx";
            s["Identity:Smtp:Password"] = SmtpPassword;
            s["Memory:Dragonfly:Configuration"] = $"127.0.0.1:1,password={DragonflyPassword},abortConnect=false";
            s["Memory:SandboxTokenKey"] = SandboxKey;
        });
        var ownerApi = await host.OwnerAsync();
        var owner = await SignInAsync(host, HostApp.OwnerEmail, HostApp.OwnerPassword);
        var ct = TestContext.Current.CancellationToken;

        var list = await ownerApi.GetStringAsync("/api/setup/guides", ct);
        var studioResponse = await ownerApi.GetAsync("/api/setup/guides/studio", ct);
        var studioJson = await studioResponse.Content.ReadAsStringAsync(ct);
        var studio = JsonDocument.Parse(studioJson).RootElement;

        Assert.Equal(HttpStatusCode.OK, studioResponse.StatusCode);
        Assert.False(studio.GetProperty("ready").GetBoolean());
        Assert.Equal(["Studio:Git:BaseUrl"], studio.GetProperty("state").GetProperty("missing").EnumerateArray().Select(m => m.GetProperty("key").GetString()));
        Assert.Contains("Studio:Git:Token", studio.GetProperty("state").GetProperty("set").EnumerateArray().Select(m => m.GetProperty("key").GetString()));
        Assert.Contains("NOT WORKING: Studio:Git:BaseUrl is not set.", studio.GetProperty("agentPrompt").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ownerApi.GetAsync("/api/setup/guides/nope", ct)).StatusCode);

        var shown = new List<string> { list, studioJson };
        foreach (var guide in SetupGuides.All)
            shown.Add(await ownerApi.GetStringAsync($"/api/setup/guides/{guide.Id}", ct));
        foreach (var page in new[] { "/SetupGuides", "/Studio", "/People", "/Debug", "/Library", "/Chat", "/Sandboxes", "/Plugins", "/ToolServers", "/Model" })
            shown.Add(await (await owner.GetAsync(page)).Content.ReadAsStringAsync(ct));

        var setupPage = shown[^10];
        Assert.Contains("Copy instructions for agent", setupPage);
        Assert.Contains("data-missing=\"Studio:Git:BaseUrl\"", setupPage);
        Assert.Contains("id=\"setup-prompt-studio\"", setupPage);
        Assert.Contains("href=\"/SetupGuides\"", setupPage);
        Assert.Contains("id=\"setup-studio\"", shown[^9]);
        // A feature that is set up shows no guide on its panel; the Setup guides page still lists it.
        Assert.Contains("id=\"setup-dragonfly\" data-setup=\"ready\"", setupPage);
        Assert.DoesNotContain("id=\"setup-dragonfly\"", shown[^7]);

        // Every configured secret, the bootstrap token and the database's connection string are absent.
        string[] values = [.. Secrets, _database, HostApp.BootstrapToken];
        foreach (var text in shown)
            foreach (var value in values)
                Assert.DoesNotContain(value, text);
    }

    [Fact]
    public async Task AnEmployee_CannotReadGuides_AndSeesOnlyAskYourAdmin()
    {
        // No KAgent:BaseUrl: the Chat guide has something missing, which is when the note shows.
        await using var host = await HostApp.StartAsync(_database, s => s.Remove("KAgent:BaseUrl"));
        var ownerApi = await host.OwnerAsync();
        await PersonAsync(host, ownerApi, "emma@skanyxx.example", "employee");
        var emma = await SignInAsync(host, "emma@skanyxx.example", Password);
        var emmaApi = await BearerAsync(host, "emma@skanyxx.example");
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Forbidden, (await emmaApi.GetAsync("/api/setup/guides", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await emmaApi.GetAsync("/api/setup/guides/chat", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync("/api/setup/guides/chat", ct)).StatusCode);
        var page = await emma.GetAsync("/SetupGuides");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(host.BaseAddress, page.Headers.Location!).AbsolutePath);

        var chat = await (await emma.GetAsync("/Chat")).Content.ReadAsStringAsync(ct);
        Assert.Contains("data-setup=\"ask-admin\"", chat);
        Assert.Contains("Ask your admin", chat);
        Assert.DoesNotContain("KAgent:BaseUrl", chat);
        Assert.DoesNotContain("port-forward", chat);
        Assert.DoesNotContain("Copy instructions for agent", chat);
        Assert.DoesNotContain("href=\"/SetupGuides\"", chat);

        var owner = await SignInAsync(host, HostApp.OwnerEmail, HostApp.OwnerPassword);
        var ownerChat = await (await owner.GetAsync("/Chat")).Content.ReadAsStringAsync(ct);
        Assert.Contains("id=\"setup-chat\" data-setup=\"missing\"", ownerChat);
        Assert.Contains("data-missing=\"KAgent:BaseUrl\"", ownerChat);
    }

    [Fact]
    public async Task ASupervisor_ReadsTheGuides()
    {
        await using var host = await HostApp.StartAsync(_database);
        var ownerApi = await host.OwnerAsync();
        await PersonAsync(host, ownerApi, "sue@skanyxx.example", "supervisor");
        var sueApi = await BearerAsync(host, "sue@skanyxx.example");
        var sue = await SignInAsync(host, "sue@skanyxx.example", Password);

        Assert.Equal(HttpStatusCode.OK, (await sueApi.GetAsync("/api/setup/guides/sandboxes", TestContext.Current.CancellationToken)).StatusCode);
        var page = await (await sue.GetAsync("/SetupGuides")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SetupGuides.All.Count, page.Split("class=\"card setup-guide\"").Length - 1);
    }

    private static async Task<Browser> SignInAsync(HostApp host, string email, string password)
    {
        var browser = new Browser(host);
        var signedIn = await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return browser;
    }

    private static async Task<HttpClient> BearerAsync(HostApp host, string email)
    {
        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password = Password });
        signIn.EnsureSuccessStatusCode();
        return host.Client((await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString());
    }

    private static async Task PersonAsync(HostApp host, HttpClient ownerApi, string email, string role)
    {
        var invite = await ownerApi.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }
}
