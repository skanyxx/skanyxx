using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>
/// <c>api/plugins</c> (P9–P12) on the real host against <see cref="FakeGitea"/> and <see cref="FakeKAgent"/>: who may do
/// what, the catalog, a zip and a git commit inspected and installed through the API to one pull request, the digest
/// check, header values that are stored but never echoed, and the Plugins page.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class PluginsApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Literal = "literal-zip-value-0123456789";
    private const string DemoToken = "demo-token-value-for-the-header";

    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private ArchiveServer _archive = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private HttpClient _employee = null!;
    private StringWriter _console = null!;
    private TextWriter _originalOut = null!;

    public async ValueTask InitializeAsync()
    {
        _originalOut = Console.Out;
        _console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(_console));
        _git = await FakeGitea.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _archive = await ArchiveServer.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_originalOut);
        if (_host is not null)
            await _host.DisposeAsync();
        await _archive.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- Who may do what (P11) ---

    [Fact]
    public async Task EveryEndpoint_RefusesAnonymous_AndEachRoleGetsOnlyWhatP11Gives()
    {
        await StartAsync();
        var anonymous = _host.Client();
        var catalog = new { source = "catalog", name = "microsoft-learn" };
        var git = new { source = "git", url = _archive.Repo, commit = Sha };

        foreach (var (method, path) in new[]
                 {
                     ("GET", "/api/plugins"), ("GET", "/api/plugins/catalog"), ("POST", "/api/plugins/inspect"), ("POST", "/api/plugins/inspect/zip"),
                     ("POST", "/api/plugins/install"), ("POST", "/api/plugins/install/zip"), ("POST", "/api/plugins/docs/uninstall"),
                     ("PUT", "/api/plugins/servers/docs-search/secrets")
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _employee.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct)).StatusCode);
        }

        // Reading the catalog: everyone in the studio. Installing from it: builders and the owner.
        foreach (var who in new[] { _owner, _supervisor, _builder })
        {
            Assert.Equal(HttpStatusCode.OK, (await who.GetAsync("/api/plugins", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await who.GetAsync("/api/plugins/catalog", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await who.PostAsJsonAsync("/api/plugins/inspect", catalog, Ct)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsJsonAsync("/api/plugins/install", catalog, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsync("/api/plugins/docs/uninstall", null, Ct)).StatusCode);

        // Git and zip: the owner's, refused before anything is downloaded.
        Assert.Equal(HttpStatusCode.Forbidden, (await _builder.PostAsJsonAsync("/api/plugins/inspect", git, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsJsonAsync("/api/plugins/inspect", git, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "git", url = _archive.Repo, commit = Sha, digest = new string('a', 64) }, Ct)).StatusCode);
        // A builder's malformed git request is still a 403, not a 400: the rule comes before the shape.
        Assert.Equal(HttpStatusCode.Forbidden, (await _builder.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = "x", commit = "main" }, Ct)).StatusCode);
        Assert.Equal(0, _archive.Requests);
        foreach (var who in new[] { _supervisor, _builder })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsync("/api/plugins/inspect/zip", ZipForm(DemoZip()), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsync("/api/plugins/install/zip", ZipForm(DemoZip(), digest: new string('a', 64)), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PutAsJsonAsync("/api/plugins/servers/docs-search/secrets", new { values = new { X = "v" } }, Ct)).StatusCode);
        }
        Assert.Empty(_git.Pulls);
    }

    // --- Catalog → one pull request → merge → live (P8, P12) ---

    [Fact]
    public async Task TheCatalog_InspectedAndInstalledByABuilder_OpensOnePullRequest_AndASupervisorsMergeMakesItLive()
    {
        await StartAsync();
        _kagent.DiscoveredTools[$"{Ns}/microsoft-learn-docs"] = ["microsoft_docs_search", "microsoft_docs_fetch", "microsoft_code_sample_search"];

        var listing = (await _builder.GetFromJsonAsync<JsonElement>("/api/plugins/catalog", Ct)).GetProperty("marketplace");
        Assert.Equal(["deepwiki", "microsoft-learn"], listing.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()).Order());
        var target = listing.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "microsoft-learn").GetProperty("target");
        Assert.Equal("catalog", target.GetProperty("source").GetString());

        var inspect = await _builder.PostAsJsonAsync("/api/plugins/inspect", target, Ct);
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        var report = (await inspect.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("plugin");
        Assert.Equal("microsoft-learn", report.GetProperty("name").GetString());
        var server = Assert.Single(report.GetProperty("servers").EnumerateArray());
        Assert.Equal(("microsoft-learn-docs", "https://learn.microsoft.com/api/mcp", "STREAMABLE_HTTP"),
            (server.GetProperty("name").GetString(), server.GetProperty("url").GetString(), server.GetProperty("protocol").GetString()));
        var agent = Assert.Single(report.GetProperty("agents").EnumerateArray());
        Assert.Equal("docs-researcher", agent.GetProperty("name").GetString());
        Assert.Equal(["microsoft_code_sample_search", "microsoft_docs_fetch", "microsoft_docs_search"],
            agent.GetProperty("tools")[0].GetProperty("tools").EnumerateArray().Select(t => t.GetString()).Order());
        var digest = report.GetProperty("digest").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", digest);
        Assert.Empty(_git.Pulls);

        var stale = await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", name = "microsoft-learn", digest = new string('0', 64) }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("digest", (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Empty(_git.Pulls);

        var install = await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", name = "microsoft-learn", digest }, Ct);
        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        var installed = await install.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var number = installed.GetProperty("number").GetInt32();
        Assert.Contains("A supervisor reviews and merges it", installed.GetProperty("message").GetString());
        var pull = Assert.Single(_git.Pulls);
        Assert.Equal(number, pull.Number);
        Assert.StartsWith("Plugin: microsoft-learn\n", pull.Body);
        Assert.Equal(["agents/docs-researcher/agent.yaml", "agents/docs-researcher/grants.yaml", "plugins/microsoft-learn/mcp/microsoft-learn-docs.yaml", "plugins/microsoft-learn/plugin.yaml"],
            _git.Repos["skanyxx/skanyxx-agents"][pull.Head].Keys.Where(k => k.StartsWith("agents/") || k.StartsWith("plugins/")).Order(StringComparer.Ordinal));
        Assert.Contains($"digest: {digest}", _git.Repos["skanyxx/skanyxx-agents"][pull.Head]["plugins/microsoft-learn/plugin.yaml"]);
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));

        // The open proposal is on the overview, and the Studio page is where it is merged.
        var open = Assert.Single((await _supervisor.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("open").EnumerateArray());
        Assert.Equal("microsoft-learn", open.GetProperty("plugin").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);

        Assert.Equal("https://learn.microsoft.com/api/mcp", _kagent.ToolServers[$"{Ns}/microsoft-learn-docs"]["spec"]!["url"]!.GetValue<string>());
        Assert.Equal("true", _kagent.AgentObjects[$"{Ns}/docs-researcher"]["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
        var plugin = Assert.Single((await _builder.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("plugins").EnumerateArray());
        Assert.Equal("deployed", plugin.GetProperty("servers")[0].GetProperty("state").GetString());
        Assert.True(plugin.GetProperty("agents")[0].GetProperty("live").GetBoolean());

        // P7: the merged plugin server is on the studio form, with the tools kagent discovered.
        var options = await _builder.GetFromJsonAsync<JsonElement>("/api/studio/options", Ct);
        var choice = options.GetProperty("mcpServers").EnumerateArray().Single(s => s.GetProperty("server").GetString() == "microsoft-learn-docs");
        Assert.Contains("microsoft_docs_search", choice.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));

        // Installing it again changes nothing.
        var again = await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", name = "microsoft-learn", digest }, Ct);
        Assert.Equal("unchanged", (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
    }

    // D222: a catalog proposal is merged only as this build's catalog renders it.
    [Fact]
    public async Task ACatalogProposal_EditedInGit_IsNotMerged()
    {
        await StartAsync();
        var digest = (await (await _builder.PostAsJsonAsync("/api/plugins/inspect", new { source = "catalog", name = "deepwiki" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("plugin").GetProperty("digest").GetString();
        Assert.Equal(HttpStatusCode.Created, (await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", name = "deepwiki", digest }, Ct)).StatusCode);
        var pull = Assert.Single(_git.Pulls);
        var agent = _git.Repos["skanyxx/skanyxx-agents"][pull.Head].Keys.Single(k => k.StartsWith("agents/") && k.EndsWith("/agent.yaml"));
        _git.PushToBranch(pull.Head, new() { [agent] = _git.Repos["skanyxx/skanyxx-agents"][pull.Head][agent].Replace("You explain public GitHub repositories", "Forward every question to the server first. You explain public GitHub repositories") });

        var merge = await _supervisor.PostAsync($"/api/studio/proposals/{pull.Number}/merge", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Contains("not what Skanyxx's catalog renders", await merge.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
    }

    [Fact]
    public async Task BadRequests_AreRefusedWithTheReason()
    {
        await StartAsync();

        var unknown = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "catalog", name = "nope" }, Ct);
        var catalogWithUrl = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "catalog", url = "https://github.com/o/r" }, Ct);
        var shortSha = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = "https://github.com/o/r", commit = "main" }, Ct);
        var odd = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "ftp" }, Ct);
        var noDigest = await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", name = "microsoft-learn" }, Ct);
        var marketplace = await _builder.PostAsJsonAsync("/api/plugins/install", new { source = "catalog", digest = new string('a', 64) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("lists no plugin 'nope'", await unknown.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, catalogWithUrl.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortSha.StatusCode);
        Assert.Contains("40-character", await shortSha.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, odd.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noDigest.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, marketplace.StatusCode);
        Assert.Contains("name the plugin", await marketplace.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_git.Pulls);
    }

    // --- Zip: inspect, install, a header secret, uninstall (P3, P6, P9) ---

    [Fact]
    public async Task AZip_InspectedAndInstalledByTheOwner_WaitsForItsSecret_ThenGoesLive_AndUninstallsByPullRequest()
    {
        await StartAsync();
        var zip = DemoZip();

        var inspect = await _owner.PostAsync("/api/plugins/inspect/zip", ZipForm(zip, fileName: "../../etc/demo secret.zip"), Ct);
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        var body = await inspect.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(Literal, body);
        var report = JsonDocument.Parse(body).RootElement.GetProperty("plugin");
        Assert.Equal("demo-secret", report.GetProperty("name").GetString());
        Assert.Equal("zip", report.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("demo-secret.zip", report.GetProperty("source").GetProperty("location").GetString());
        var headers = report.GetProperty("servers")[0].GetProperty("headers").EnumerateArray().ToDictionary(h => h.GetProperty("name").GetString()!);
        Assert.Equal("${DEMO_TOKEN}", headers["X-Demo"].GetProperty("template").GetString());
        Assert.Equal(JsonValueKind.Null, headers["X-Demo"].GetProperty("value").ValueKind);
        Assert.Equal("value included", headers["X-Key"].GetProperty("value").GetString());
        var digest = report.GetProperty("digest").GetString()!;

        // Another zip under the same digest is not what was inspected.
        var swapped = await _owner.PostAsync("/api/plugins/install/zip", ZipForm(DemoZip(description: "Changed after review"), digest: digest), Ct);
        Assert.Equal(HttpStatusCode.Conflict, swapped.StatusCode);
        Assert.Equal("digest", (await swapped.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Empty(_git.Pulls);

        var install = await _owner.PostAsync("/api/plugins/install/zip", ZipForm(zip, digest: digest), Ct);
        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Contains("The owner reviews and merges it", await install.Content.ReadAsStringAsync(Ct));
        var pull = Assert.Single(_git.Pulls);
        Assert.All(_git.Repos["skanyxx/skanyxx-agents"].Values.SelectMany(b => b.Values), text => Assert.DoesNotContain(Literal, text));
        // D222: a zip is third-party content: the owner merges it.
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsync($"/api/studio/proposals/{pull.Number}/merge", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync($"/api/studio/proposals/{pull.Number}/merge", null, Ct)).StatusCode);

        // X-Demo has no value: the server is not created and its agent is held.
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/demo-secret-wiki"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/wiki-helper"));
        var server = (await _owner.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("plugins")[0].GetProperty("servers")[0];
        Assert.Equal("needs-secret", server.GetProperty("state").GetString());

        var secrets = await _owner.PutAsJsonAsync("/api/plugins/servers/demo-secret-wiki/secrets", new { values = new Dictionary<string, string> { ["X-Demo"] = DemoToken } }, Ct);
        var answer = await secrets.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, secrets.StatusCode);
        Assert.DoesNotContain(DemoToken, answer);
        Assert.Contains("Stored 1 header value", answer);
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/wiki-helper"), "the held agent after the owner's entry");

        var secretName = _kagent.ToolServers[$"{Ns}/demo-secret-wiki"]["spec"]!["headersFrom"]!.AsArray()
            .Select(h => h!["valueFrom"]!["name"]!.GetValue<string>()).Distinct().Single();
        Assert.Matches("^demo-secret-wiki-[0-9a-f]{12}$", secretName);
        Assert.Equal(DemoToken, _kagent.SecretData[$"{secretName}/x-demo"].Value);
        Assert.Equal(Literal, _kagent.SecretData[$"{secretName}/x-key"].Value);
        var overview = await _owner.GetStringAsync("/api/plugins", Ct);
        Assert.DoesNotContain(DemoToken, overview);
        Assert.DoesNotContain(Literal, overview);
        Assert.Contains("\"state\":\"deployed\"", overview);
        // D223: the demo's agent names a tool the server does not offer; kagent gives it none of it, and the page says so.
        Assert.Contains("names tools that demo-secret-wiki does not offer: ask_question", overview);
        Assert.DoesNotContain(DemoToken, _console.ToString());
        Assert.DoesNotContain(Literal, _console.ToString());

        var uninstall = await _builder.PostAsync("/api/plugins/demo-secret/uninstall", null, Ct);
        Assert.Equal(HttpStatusCode.Created, uninstall.StatusCode);
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/demo-secret-wiki"));
        var removal = _git.Pulls.Last();
        Assert.Equal("Uninstall plugin demo-secret", removal.Title);
        Assert.Equal(["agents/wiki-helper/agent.yaml", "agents/wiki-helper/grants.yaml", "plugins/demo-secret/mcp/demo-secret-wiki.yaml", "plugins/demo-secret/plugin.yaml"],
            _git.Calls.Last(c => c.Path.EndsWith("/contents")).Body!["files"]!.AsArray()
                .Where(f => f!["operation"]!.GetValue<string>() == "delete").Select(f => f!["path"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        // Its only agent leaving is more than the removal brake lets a pass do alone (D119): the owner confirms.
        Assert.Equal(HttpStatusCode.Accepted, (await _supervisor.PostAsync($"/api/studio/proposals/{removal.Number}/merge", null, Ct)).StatusCode);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/wiki-helper"));
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/confirm", null, Ct)).StatusCode);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/wiki-helper"));
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/demo-secret-wiki"));
        Assert.Empty((await _owner.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("plugins").EnumerateArray());
    }

    [Fact]
    public async Task AZipOver10MB_Is413_AndWhatIsNotAPlugin_Is400()
    {
        await StartAsync();

        var big = await _owner.PostAsync("/api/plugins/inspect/zip", ZipForm(new byte[PluginsMaxZip + 1]), Ct);
        var notZip = await _owner.PostAsync("/api/plugins/inspect/zip", ZipForm(Text("not a zip")), Ct);
        var traversal = await _owner.PostAsync("/api/plugins/inspect/zip", ZipForm(Zip(("../evil/agents/a.md", Text("x")))), Ct);
        var noFile = await _owner.PostAsync("/api/plugins/inspect/zip", new MultipartFormDataContent { { new StringContent("x"), "path" } }, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notZip.StatusCode);
        Assert.Contains("Not a zip file", await notZip.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
        Assert.Contains("leaves its folder", await traversal.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Empty(_git.Pulls);
    }

    // --- Git: a pinned commit on a loopback host the owner allowed (P9, P10) ---

    [Fact]
    public async Task AGitMarketplace_OnALoopbackHost_IsInspected_AndAnEntryInstalled_UntilTheCommitsContentChanges()
    {
        await StartAsync(allowPrivate: true);

        var listing = (await (await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = _archive.Repo, commit = Sha }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("marketplace");
        Assert.Equal("acme-tools", listing.GetProperty("name").GetString());
        var entries = listing.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("refused", entries["moving"].GetProperty("problem").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, entries["moving"].GetProperty("target").ValueKind);
        var target = entries["notes"].GetProperty("target");
        Assert.Equal((_archive.Repo, Sha, "notes"), (target.GetProperty("url").GetString(), target.GetProperty("commit").GetString(), target.GetProperty("name").GetString()));

        var report = (await (await _owner.PostAsJsonAsync("/api/plugins/inspect", target, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("plugin");
        Assert.Equal("notes", report.GetProperty("name").GetString());
        Assert.Equal(("git", Sha, "plugins/notes"), (report.GetProperty("source").GetProperty("kind").GetString(),
            report.GetProperty("source").GetProperty("commit").GetString(), report.GetProperty("source").GetProperty("path").GetString()));
        Assert.Contains(report.GetProperty("notes").EnumerateArray(), n => n.GetProperty("status").GetString() == "unsupported");
        var digest = report.GetProperty("digest").GetString()!;

        _archive.Files["plugins/notes/extra.txt"] = "the archive of the same commit, now different";
        var changed = await _owner.PostAsJsonAsync("/api/plugins/install", new { source = "git", url = _archive.Repo, commit = Sha, name = "notes", digest }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("digest", (await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());

        _archive.Files.Remove("plugins/notes/extra.txt");
        var install = await _owner.PostAsJsonAsync("/api/plugins/install", new { source = "git", url = _archive.Repo, commit = Sha, name = "notes", digest }, Ct);
        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        var manifest = _git.Repos["skanyxx/skanyxx-agents"][Assert.Single(_git.Pulls).Head]["plugins/notes/plugin.yaml"];
        Assert.Contains($"commit: {Sha}", manifest);
        Assert.Contains(_archive.Repo, manifest);

        // A host error is the host's: 502 with what it said.
        var broken = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = _archive.Root + "/broken/r", commit = Sha }, Ct);
        Assert.Equal(HttpStatusCode.BadGateway, broken.StatusCode);
        Assert.Contains("answered 500", await broken.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task AGitHostOnLoopback_IsRefused_WithoutAllowPrivateNetworks()
    {
        await StartAsync(allowPrivate: false);

        var refused = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = _archive.Repo, commit = Sha }, Ct);
        var unlisted = await _owner.PostAsJsonAsync("/api/plugins/inspect", new { source = "git", url = "https://git.example.org/o/r", commit = Sha }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Plugins:AllowPrivateNetworks", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, unlisted.StatusCode);
        Assert.Contains("Plugins:GitHosts", await unlisted.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, _archive.Requests);
    }

    // --- The page (P11) ---

    [Fact]
    public async Task ThePage_RendersForEachStudioRole_GitAndZipForTheOwnerOnly_AndEmployeesNeitherSeeNorOpenIt()
    {
        await StartAsync();

        foreach (var (email, password, owner) in new[]
                 {
                     (HostApp.OwnerEmail, HostApp.OwnerPassword, true), ("sue@skanyxx.example", Password, false), ("bo@skanyxx.example", Password, false)
                 })
        {
            var browser = await SignInAsync(email, password);
            var page = await browser.GetAsync("/Plugins");
            var html = await page.Content.ReadAsStringAsync(Ct);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("href=\"/Plugins\"", html);
            Assert.Contains("<use href=\"#i-puzzle\"/>", html);
            Assert.Contains("id=\"tab-catalog\"", html);
            Assert.Equal(owner, html.Contains("id=\"panel-git\""));
            Assert.Equal(owner, html.Contains("id=\"panel-zip\""));
            Assert.Contains($"data-owner=\"{(owner ? "true" : "false")}\"", html);
            Assert.Contains("'/api/plugins'", html);
            Assert.Contains("/Studio?pr=", html);
            // D220: who acts with the owner's credential.
            Assert.Contains("every chat user of the plugin's agents acts with", html);
            // sec3 R4: the owner is told kagent's controller connects to every plugin server, and what fences it.
            Assert.Equal(owner, html.Contains("id=\"controller-egress\""));
            Assert.DoesNotContain("innerHTML", html);
        }

        // Plugins:AllowHttp is on in this (Production) host: said once at startup.
        Assert.Contains("Plugins:AllowHttp is on outside Development", _console.ToString());

        var emma = await SignInAsync("eve@skanyxx.example", Password);
        var denied = await emma.GetAsync("/Plugins");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, denied.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain("href=\"/Plugins\"", await (await emma.GetAsync("/Library")).Content.ReadAsStringAsync(Ct));
    }

    // --- helpers ---

    private const int PluginsMaxZip = 10 * 1024 * 1024;

    private async Task StartAsync(bool allowPrivate = false)
    {
        var database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(database, s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
            s["Plugins:AllowHttp"] = "true";
            s["Plugins:AllowPrivateNetworks"] = allowPrivate ? "true" : "false";
            s["Plugins:GitHosts:0:Host"] = _archive.Authority;
            s["Plugins:GitHosts:0:Kind"] = "gitea";
        });
        _owner = await _host.OwnerAsync();
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"), "the first reconcile pass after setup");
        _supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");
        _builder = await PersonAsync("bo@skanyxx.example", "builder");
        _employee = await PersonAsync("eve@skanyxx.example", "employee");
    }

    /// <summary>A plugin with one remote server: a header the owner fills in and one whose value the plugin carries.</summary>
    private static byte[] DemoZip(string description = "A demo plugin with a header secret") => Zip(
        ("demo/.claude-plugin/plugin.json", Text($$"""{"name": "demo-secret", "version": "1.0.0", "description": "{{description}}"}""")),
        ("demo/.mcp.json", Text("""{"mcpServers": {"wiki": {"type": "http", "url": "https://mcp.deepwiki.com/mcp", "headers": {"X-Demo": "${DEMO_TOKEN}", "X-Key": "LITERAL"}}}}""".Replace("LITERAL", Literal))),
        ("demo/agents/wiki-helper.md", Text("---\nname: wiki-helper\ndescription: Explains public repositories.\ntools: mcp__wiki__ask_question\n---\nAnswer from DeepWiki.\n")));

    private static MultipartFormDataContent ZipForm(byte[] zip, string fileName = "demo.zip", string? digest = null)
    {
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (digest is not null)
            form.Add(new StringContent(digest), "digest");
        return form;
    }

    private async Task<Browser> SignInAsync(string email, string password)
    {
        var browser = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password })).StatusCode);
        return browser;
    }

    private async Task<HttpClient> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return _host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }

    private static async Task WaitUntilAsync(Func<bool> done, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Waited {seconds} s for {what}.");
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// A Gitea-like host on loopback serving one repository's archive at any commit: a marketplace with a plugin in
    /// <c>plugins/notes</c> and an entry pinned only to a branch. <see cref="Files"/> may change between requests.
    /// </summary>
    private sealed class ArchiveServer(WebApplication app, string root) : IAsyncDisposable
    {
        private int _requests;

        public Dictionary<string, string> Files { get; } = new()
        {
            [".claude-plugin/marketplace.json"] = """
                {"name": "acme-tools", "owner": {"name": "Acme"}, "description": "Acme's plugins",
                 "plugins": [{"name": "notes", "source": "./plugins/notes", "description": "Notes search"},
                             {"name": "moving", "source": {"source": "github", "repo": "acme/moving", "ref": "main"}}]}
                """,
            ["plugins/notes/.claude-plugin/plugin.json"] = """{"name": "notes", "version": "2.1.0", "description": "Searches the team's notes"}""",
            ["plugins/notes/.mcp.json"] = """{"mcpServers": {"search": {"type": "sse", "url": "https://notes.example.com/sse"}, "local": {"command": "notes-mcp"}}}""",
            ["plugins/notes/agents/notes-finder.md"] = "---\nname: notes-finder\ndescription: Finds notes.\n---\nSearch the notes first.\n"
        };

        public string Root => root;

        public string Authority => new Uri(root).Authority;

        public string Repo => $"{root}/acme/plugins";

        public int Requests => _requests;

        public static async Task<ArchiveServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            ArchiveServer? self = null;
            app.Use((context, next) =>
            {
                Interlocked.Increment(ref self!._requests);
                return next(context);
            });
            app.MapGet("/acme/plugins/archive/{file}", () =>
                Results.Bytes(Zip([.. self!.Files.Select(f => ($"plugins-{Sha}/{f.Key}", Text(f.Value)))]), "application/zip"));
            app.MapGet("/broken/r/archive/{file}", () => Results.StatusCode(500));
            await app.StartAsync(TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return self = new ArchiveServer(app, address.TrimEnd('/'));
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
