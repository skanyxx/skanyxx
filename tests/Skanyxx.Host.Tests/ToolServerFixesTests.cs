using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Skanyxx.Host.Tests.ToolServersApiTests;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Every part of the Tool Servers page that can be not working says so in plain words, with numbered steps for a local
/// run and for Helm, how to verify, and a prompt for an AI coding agent. None of it carries a header value or kagent's
/// own error text (D313).
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ToolServerFixesTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
        });
        _owner = await _host.OwnerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    /// <summary>A fix is whole: a status line, local and Helm steps, a check, and an agent prompt naming the goal and state.</summary>
    private static void AssertWhole(JsonElement fix, string code, params string[] promptMentions)
    {
        var text = fix.GetRawText();
        Assert.Equal(code, fix.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(fix.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(fix.GetProperty("cause").GetString()));
        foreach (var part in new[] { "local", "helm", "verify" })
            Assert.NotEmpty(fix.GetProperty(part).EnumerateArray());
        Assert.Contains(fix.GetProperty("local").EnumerateArray(), s => s.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String);
        var prompt = fix.GetProperty("agentPrompt").GetString()!;
        foreach (var section in new[] { "Goal:", "Detected state (names only, no secret values):", "Steps: local development:", "Steps: Helm install:", "Verify:", "Rules:" })
            Assert.Contains(section, prompt);
        foreach (var mention in promptMentions)
            Assert.Contains(mention, prompt);
        Assert.DoesNotContain("FAKE-SECRET", text);
        Assert.DoesNotContain(FakeKAgent.InternalDetail, text);
        Assert.DoesNotContain("--from-literal", text); // a value on the command line ends up in the shell history
    }

    private async Task<JsonElement> ServerAsync(string reference) => Server(await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct), reference);

    private async Task CreateAsync(object form) => (await _owner.PostAsJsonAsync("/api/toolservers", form, Ct)).EnsureSuccessStatusCode();

    [Fact]
    public async Task KagentOnThisMachine_NotAnswering_IsTheMissingPortForward()
    {
        // HostApp's default kagent address: 127.0.0.1:1, where nothing listens.
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());

        var overview = await (await host.OwnerAsync()).GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);
        var kagent = overview.GetProperty("kAgent");

        Assert.False(kagent.GetProperty("reachable").GetBoolean());
        AssertWhole(kagent.GetProperty("fix"), "kagent-port-forward", "http://127.0.0.1:1", "KAgent:BaseUrl");
        Assert.Contains("scripts/dev/first-hour.sh up", kagent.GetRawText());
        Assert.Contains("kubectl -n kagent port-forward svc/kagent-controller 8083:8083", kagent.GetRawText());
        Assert.Empty(overview.GetProperty("servers").EnumerateArray());
    }

    [Fact]
    public async Task KagentElsewhere_NotAnswering_IsUnreachable()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "kagent-controller.invalid";
            s["KAgent:Port"] = "8083";
            s["KAgent:ControlTimeoutSeconds"] = "3";
        });

        var kagent = (await (await host.OwnerAsync()).GetFromJsonAsync<JsonElement>("/api/toolservers", Ct)).GetProperty("kAgent");

        AssertWhole(kagent.GetProperty("fix"), "kagent-unreachable", "http://kagent-controller.invalid:8083");
        Assert.Contains("skanyxx.kagent.url", kagent.GetRawText());
        Assert.Contains("networkPolicy.skanyxxEgress.extra", kagent.GetRawText());
    }

    [Fact]
    public async Task KagentsAddress_IsShownWithoutCredentials()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s => s["KAgent:IngressUrl"] = "http://bot:ingress-pass@127.0.0.1:1/kagent");

        var kagent = (await (await host.OwnerAsync()).GetFromJsonAsync<JsonElement>("/api/toolservers", Ct)).GetProperty("kAgent");

        Assert.Equal("http://127.0.0.1:1", kagent.GetProperty("address").GetString());
        Assert.DoesNotContain("ingress-pass", kagent.GetRawText());
    }

    [Fact]
    public async Task KagentAnsweringAnError_IsRefused_WithoutItsText()
    {
        await CreateAsync(Form("docs", "http://docs.tools.svc:8080/mcp"));
        _kagent.ToolServersStatus = 500;

        var overview = await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);
        var refresh = await _owner.PostAsync("/api/toolservers/team-a/docs/refresh", null, Ct);

        AssertWhole(overview.GetProperty("kAgent").GetProperty("fix"), "kagent-refused", "kagent answered HTTP 500");
        // Skanyxx's own record still lists the server; its status is unknown until kagent answers.
        Assert.Equal("unknown", Server(overview, "team-a/docs").GetProperty("status").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadGateway, refresh.StatusCode);
        Assert.DoesNotContain(FakeKAgent.InternalDetail, await refresh.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task AServerKagentDoesNotList_IsNotRegistered()
    {
        await CreateAsync(Form("docs", "http://docs.tools.svc:8080/mcp"));
        _kagent.ToolServers.TryRemove("team-a/docs", out _); // kagent never reconciled it

        var server = await ServerAsync("team-a/docs");

        Assert.Equal("not-registered", server.GetProperty("status").GetProperty("code").GetString());
        AssertWhole(server.GetProperty("fixes")[0], "not-registered", "team-a/docs");
        Assert.Contains("kubectl -n team-a get remotemcpserver docs -o yaml", server.GetRawText());
    }

    [Fact]
    public async Task AServerAddedElsewhere_WithNoTools_IsDiscoveryFailed()
    {
        _kagent.ListedToolServers.Enqueue(new JsonObject { ["ref"] = "ops/external", ["groupKind"] = "RemoteMCPServer.kagent.dev", ["discoveredTools"] = new JsonArray() });

        var server = await ServerAsync("ops/external");

        Assert.Equal("other", server.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, server.GetProperty("url").ValueKind);
        Assert.False(server.GetProperty("canEdit").GetBoolean());
        Assert.True(server.GetProperty("canDelete").GetBoolean());
        AssertWhole(server.GetProperty("fixes")[0], "discovery-failed", "ops/external");
        Assert.Contains("status.conditions", server.GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await _owner.PutAsJsonAsync("/api/toolservers/ops/external", Form("external", "http://x.example/mcp"), Ct)).StatusCode);
    }

    [Fact]
    public async Task ALocalhostUrl_IsUnreachableFromThePods()
    {
        await CreateAsync(Form("local", "http://localhost:9000/mcp"));
        _kagent.DiscoveredTools["team-a/local"] = [];

        var server = await ServerAsync("team-a/local");
        var fix = server.GetProperty("fixes")[0];

        Assert.Equal("server-unreachable", server.GetProperty("status").GetProperty("code").GetString());
        AssertWhole(fix, "server-unreachable", "http://localhost:9000", "loopback");
        Assert.Contains("host.docker.internal", fix.GetRawText());
        Assert.DoesNotContain("/mcp", fix.GetProperty("agentPrompt").GetString()!.Split("Server address:")[1].Split('\n')[0]); // the path is left out
    }

    [Fact]
    public async Task NoTools_WithAHeaderFromAnExistingSecret_PointsAtTheSecret_ByNameOnly()
    {
        await CreateAsync(Form("jira", "http://jira-mcp.tools.svc:8080/mcp",
            headers: [SecretHeader("Authorization", "jira-token", "token"), ValueHeader("X-Api-Key", SecretValue)]));
        _kagent.DiscoveredTools["team-a/jira"] = [];

        var server = await ServerAsync("team-a/jira");
        var fix = server.GetProperty("fixes")[0];

        Assert.Equal("header-secret", server.GetProperty("status").GetProperty("code").GetString());
        AssertWhole(fix, "header-secret", "Header Authorization is read from Secret jira-token, key token");
        Assert.Contains("get secret jira-token -o go-template", fix.GetRawText()); // keys only, never the data
        Assert.Contains("--from-file=token=", fix.GetRawText());
        Assert.Contains(server.GetProperty("fixes").EnumerateArray(), f => f.GetProperty("code").GetString() == "server-unreachable");
        Assert.DoesNotContain("FAKE-SECRET", server.GetRawText());
    }

    [Fact]
    public async Task NoTools_FromAnHttpsServer_ExplainsTlsAndTheUrl()
    {
        await CreateAsync(Form("secure", "https://mcp.corp.example:8443/v1/mcp"));
        _kagent.DiscoveredTools["team-a/secure"] = [];

        var server = await ServerAsync("team-a/secure");
        var fix = server.GetProperty("fixes")[0];

        Assert.Equal("tls", server.GetProperty("status").GetProperty("code").GetString());
        AssertWhole(fix, "tls", "https://mcp.corp.example:8443");
        Assert.Contains("caCertSecretRef", fix.GetRawText());
        Assert.DoesNotContain("/v1/mcp", fix.GetProperty("agentPrompt").GetString());
    }

    [Fact]
    public async Task AStdioServer_IsListedWithItsTools_ButNotManaged()
    {
        _kagent.ListedToolServers.Enqueue(new JsonObject
        {
            ["ref"] = "kagent/filesystem", ["groupKind"] = "MCPServer.kagent.dev",
            ["discoveredTools"] = new JsonArray(new JsonObject { ["name"] = "read_file", ["description"] = "Reads a file" })
        });

        var server = await ServerAsync("kagent/filesystem");
        var delete = await _owner.DeleteAsync("/api/toolservers/kagent/filesystem?confirmInUse=true", Ct);
        var edit = await _owner.PutAsJsonAsync("/api/toolservers/kagent/filesystem", Form("filesystem", "http://x.example/mcp"), Ct);

        Assert.Equal("Stdio MCP server", server.GetProperty("kindLabel").GetString());
        Assert.False(server.GetProperty("supported").GetBoolean());
        Assert.False(server.GetProperty("canDelete").GetBoolean());
        Assert.Equal("Reads a file", server.GetProperty("tools")[0].GetProperty("description").GetString());
        AssertWhole(server.GetProperty("fixes")[0], "stdio-unsupported", "kagent/filesystem");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("stdio", (await delete.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers"));
    }

    [Fact]
    public async Task AStudioServer_IsChangedInStudioOnly()
    {
        var server = await ServerAsync("kagent/skanyxx-memory-seed");
        var delete = await _owner.DeleteAsync("/api/toolservers/kagent/skanyxx-memory-seed?confirmInUse=true", Ct);

        Assert.Equal("studio", server.GetProperty("source").GetString());
        Assert.False(server.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("studio", (await delete.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers"));
    }

    [Fact]
    public async Task AWorkingServer_HasNoFix()
    {
        await CreateAsync(Form("docs", "https://docs.example/mcp"));

        var server = await ServerAsync("team-a/docs");

        Assert.Equal("ready", server.GetProperty("status").GetProperty("code").GetString());
        Assert.Equal("Ready: 2 tools", server.GetProperty("status").GetProperty("text").GetString());
        Assert.Empty(server.GetProperty("fixes").EnumerateArray());
        Assert.True((await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct)).GetProperty("kAgent").GetProperty("reachable").GetBoolean());
    }
}
