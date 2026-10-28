using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The Tool Servers page's API against <see cref="FakeKAgent"/> (D313): add, edit, delete and refresh RemoteMCPServers in
/// kagent's own shape; the owner writes, a supervisor reads and refreshes, nobody else gets in; a header value goes to
/// kagent once and is never returned, logged, audited or stored. Each not-working state: <c>ToolServerFixesTests</c>.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ToolServersApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    internal const string Password = "a long member passphrase";
    internal const string SecretValue = "Bearer FAKE-SECRET-7d1f0c9a2b";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private string _database = null!;
    private string _log = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _database = await fixture.NewDatabaseAsync();
        _log = Path.Combine(Path.GetTempPath(), $"skanyxx-toolservers-{Guid.NewGuid():N}.log");
        _host = await HostApp.StartAsync(_database, s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            WithLogFile(s, _log);
        });
        _owner = await _host.OwnerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        File.Delete(_log);
    }

    /// <summary>Every log event at Debug and above, also into <paramref name="path"/> (Serilog's file sink, unbuffered).</summary>
    internal static void WithLogFile(Dictionary<string, string> settings, string path)
    {
        settings["Serilog:Using:0"] = "Serilog.Sinks.File";
        settings["Serilog:MinimumLevel:Default"] = "Debug";
        settings["Serilog:WriteTo:0:Name"] = "File";
        settings["Serilog:WriteTo:0:Args:path"] = path;
    }

    internal static object Form(string name, string url, string ns = "team-a", string protocol = "STREAMABLE_HTTP", int? timeout = null, params object[] headers) =>
        new { name, @namespace = ns, url, protocol, timeoutSeconds = timeout, description = "Docs search", headers };

    internal static object ValueHeader(string name, string value) => new { name, source = "value", value };

    internal static object SecretHeader(string name, string secretName, string secretKey) => new { name, source = "secret", secretName, secretKey };

    internal static JsonElement Server(JsonElement overview, string reference) =>
        overview.GetProperty("servers").EnumerateArray().Single(s => s.GetProperty("ref").GetString() == reference);

    [Fact]
    public async Task Create_SendsKagentsRemoteMcpServer_WithHeadersFromSecrets_AndTheListShowsIt()
    {
        var created = await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "https://mcp.example:8443/mcp", protocol: "SSE", timeout: 45,
            headers: [ValueHeader("Authorization", SecretValue), SecretHeader("X-Tenant", "tenant-secret", "tenant")]), Ct);
        var duplicate = await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "https://mcp.example:8443/mcp"), Ct);
        var listed = await _owner.GetAsync("/api/toolservers", Ct);
        var listText = await listed.Content.ReadAsStringAsync(Ct);
        var server = Server(JsonDocument.Parse(listText).RootElement, "team-a/docs");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("team-a/docs", (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("ref").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var stored = _kagent.ToolServers["team-a/docs"];
        Assert.Equal("https://mcp.example:8443/mcp", stored["spec"]!["url"]!.GetValue<string>());
        Assert.Equal("SSE", stored["spec"]!["protocol"]!.GetValue<string>());
        Assert.Equal("45s", stored["spec"]!["timeout"]!.GetValue<string>());
        Assert.Equal("skanyxx-toolservers", stored["metadata"]!["labels"]!["skanyxx.dev/managed-by"]!.GetValue<string>());
        var headers = stored["spec"]!["headersFrom"]!.AsArray();
        var companion = headers[0]!["valueFrom"]!["name"]!.GetValue<string>();
        Assert.StartsWith("docs-headers-", companion);
        Assert.Equal("tenant-secret", headers[1]!["valueFrom"]!["name"]!.GetValue<string>());
        Assert.Equal("tenant", headers[1]!["valueFrom"]!["key"]!.GetValue<string>());
        // kagent received the value, once, into the Secret it keeps with the server.
        Assert.Equal(SecretValue, _kagent.SecretData[$"{companion}/Authorization"].Value);
        Assert.DoesNotContain(SecretValue, stored.ToJsonString());

        Assert.Equal("https://mcp.example:8443/mcp", server.GetProperty("url").GetString());
        Assert.Equal("SSE", server.GetProperty("protocol").GetString());
        Assert.Equal(45, server.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal("Remote MCP server", server.GetProperty("kindLabel").GetString());
        Assert.Equal("ready", server.GetProperty("status").GetProperty("code").GetString());
        Assert.True(server.GetProperty("canEdit").GetBoolean());
        Assert.True(server.GetProperty("canDelete").GetBoolean());
        Assert.Equal(["memory_search", "memory_upsert"], server.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(FakeKAgent.ToolDescriptionPrefix + "memory_search", server.GetProperty("tools")[0].GetProperty("description").GetString());
        Assert.Equal("value", server.GetProperty("headers")[0].GetProperty("source").GetString());
        Assert.Equal(companion, server.GetProperty("headers")[0].GetProperty("secretName").GetString());
        Assert.Equal("secret", server.GetProperty("headers")[1].GetProperty("source").GetString());
        Assert.DoesNotContain(SecretValue, listText);
        Assert.True(JsonDocument.Parse(listText).RootElement.GetProperty("canWrite").GetBoolean());
    }

    [Fact]
    public async Task Edit_ReplacesTheServer_WithAFreshCompanionSecret_AndAskForTheValueAgain()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp", headers: [ValueHeader("Authorization", SecretValue)]), Ct)).EnsureSuccessStatusCode();
        var first = _kagent.ToolServers["team-a/docs"]["spec"]!["headersFrom"]![0]!["valueFrom"]!["name"]!.GetValue<string>();

        var withoutValue = await _owner.PutAsJsonAsync("/api/toolservers/team-a/docs",
            Form("ignored", "http://docs2.tools.svc:8080/mcp", ns: "ignored", headers: [new { name = "Authorization", source = "value" }]), Ct);
        var edited = await _owner.PutAsJsonAsync("/api/toolservers/team-a/docs",
            Form("ignored", "http://docs2.tools.svc:8080/mcp", ns: "ignored", headers: [ValueHeader("Authorization", SecretValue + "-2")]), Ct);
        var server = Server(await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct), "team-a/docs");
        var second = _kagent.ToolServers["team-a/docs"]["spec"]!["headersFrom"]![0]!["valueFrom"]!["name"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.BadRequest, withoutValue.StatusCode);
        Assert.Contains("enter its value", await withoutValue.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Single(_kagent.CallsTo("DELETE", "/api/toolservers/team-a/docs"));
        Assert.Equal("http://docs2.tools.svc:8080/mcp", _kagent.ToolServers["team-a/docs"]["spec"]!["url"]!.GetValue<string>());
        Assert.NotEqual(first, second);
        Assert.False(_kagent.Secrets.ContainsKey(first));
        Assert.Equal(SecretValue + "-2", _kagent.SecretData[$"{second}/Authorization"].Value);
        Assert.Equal("http://docs2.tools.svc:8080/mcp", server.GetProperty("url").GetString());
        Assert.False(_kagent.ToolServers.ContainsKey("ignored/ignored"));
    }

    [Fact]
    public async Task Delete_OfAServerAgentsUse_AsksToConfirm_ThenDeletes()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp", ns: "kagent"), Ct)).EnsureSuccessStatusCode();
        _kagent.AgentObjects["kagent/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "helper", ["namespace"] = "kagent" },
            ["spec"] = new JsonObject
            {
                ["type"] = "Declarative",
                ["declarative"] = new JsonObject
                {
                    ["tools"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "McpServer",
                        ["mcpServer"] = new JsonObject { ["name"] = "docs", ["kind"] = "RemoteMCPServer", ["toolNames"] = new JsonArray("memory_search") }
                    })
                }
            }
        };
        var usedBy = Server(await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct), "kagent/docs").GetProperty("usedBy");

        var refused = await _owner.DeleteAsync("/api/toolservers/kagent/docs", Ct);
        var refusal = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var stillThere = _kagent.ToolServers.ContainsKey("kagent/docs");
        var deleted = await _owner.DeleteAsync("/api/toolservers/kagent/docs?confirmInUse=true", Ct);
        var after = await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);
        var again = await _owner.DeleteAsync("/api/toolservers/kagent/docs?confirmInUse=true", Ct);

        Assert.Equal(["kagent/helper"], usedBy.EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("in-use", refusal.GetProperty("reason").GetString());
        Assert.Equal("kagent/helper", refusal.GetProperty("current").GetProperty("usedBy")[0].GetString());
        Assert.True(stillThere);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False(_kagent.ToolServers.ContainsKey("kagent/docs"));
        Assert.DoesNotContain(after.GetProperty("servers").EnumerateArray(), s => s.GetProperty("ref").GetString() == "kagent/docs");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Delete_OfAnUnusedServer_NeedsNoConfirmation()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp"), Ct)).EnsureSuccessStatusCode();

        var deleted = await _owner.DeleteAsync("/api/toolservers/team-a/docs", Ct);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False(_kagent.ToolServers.ContainsKey("team-a/docs"));
    }

    [Fact]
    public async Task Refresh_ReadsWhatKagentDiscoveredNow()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp"), Ct)).EnsureSuccessStatusCode();
        _kagent.DiscoveredTools["team-a/docs"] = [];
        var before = await (await _owner.PostAsync("/api/toolservers/team-a/docs/refresh", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        _kagent.DiscoveredTools["team-a/docs"] = ["search_docs"];

        var refreshed = await _owner.PostAsync("/api/toolservers/team-a/docs/refresh", null, Ct);
        var after = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var missing = await _owner.PostAsync("/api/toolservers/team-a/nope/refresh", null, Ct);

        Assert.Equal("server-unreachable", before.GetProperty("status").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("ready", after.GetProperty("status").GetProperty("code").GetString());
        Assert.Equal("search_docs", after.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Empty(after.GetProperty("fixes").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task KagentFailingTheCreate_Is502WithAFixedText_AndNothingIsSaved()
    {
        _kagent.FailToolServerCreates = 1;

        var response = await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp", headers: [ValueHeader("Authorization", SecretValue)]), Ct);
        var list = await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("restarting", text); // kagent's text stays in the log
        Assert.DoesNotContain(SecretValue, text);
        Assert.DoesNotContain(list.GetProperty("servers").EnumerateArray(), s => s.GetProperty("ref").GetString() == "team-a/docs");
    }

    [Fact]
    public async Task Edit_WhenKagentDoesNotRecreate_Is502_AndThePageShowsNotRegistered()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp"), Ct)).EnsureSuccessStatusCode();
        _kagent.FailToolServerCreates = 1;

        var edited = await _owner.PutAsJsonAsync("/api/toolservers/team-a/docs",
            Form("docs", "http://docs2.tools.svc:8080/mcp", headers: [ValueHeader("Authorization", SecretValue)]), Ct);
        var text = await edited.Content.ReadAsStringAsync(Ct);
        var server = Server(await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct), "team-a/docs");
        var failed = await _owner.GetStringAsync("/api/identity/audit?action=toolserver.write_failed", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, edited.StatusCode);
        Assert.Contains("Save again", text);
        Assert.DoesNotContain("restarting", text);
        Assert.DoesNotContain("FAKE-SECRET", text);
        Assert.Equal("not-registered", server.GetProperty("status").GetProperty("code").GetString());
        Assert.Equal("http://docs2.tools.svc:8080/mcp", server.GetProperty("url").GetString());
        Assert.Contains("update: re-create", failed);
    }

    [Theory]
    [InlineData("skanyxx-memory-helper")]
    public async Task Create_OfAStudioName_IsRefused(string name)
    {
        var response = await _owner.PostAsJsonAsync("/api/toolservers", Form(name, "http://x.tools.svc/mcp", ns: "kagent"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("studio", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
    }

    [Fact]
    public async Task Create_WithNullHeaders_Is400Or201_NeverA500()
    {
        var nulls = await _owner.PostAsync("/api/toolservers", JsonContent.Create(new JsonObject
        {
            ["name"] = "docs", ["url"] = "http://docs.tools.svc/mcp", ["headers"] = new JsonArray((JsonNode?)null)
        }), Ct);
        var none = await _owner.PostAsync("/api/toolservers", JsonContent.Create(new JsonObject
        {
            ["name"] = "docs2", ["url"] = "http://docs.tools.svc/mcp", ["headers"] = null
        }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, nulls.StatusCode);
        Assert.Equal(HttpStatusCode.Created, none.StatusCode);
    }

    [Fact]
    public async Task ASupervisor_SeesWhereAServerPoints_NotItsPath()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("hosted", "https://mcp.hosted.example/s/k3y-in-path/mcp"), Ct)).EnsureSuccessStatusCode();
        var supervisor = await PersonAsync(_host, _owner, "sue@skanyxx.example", "supervisor");

        var list = await supervisor.GetStringAsync("/api/toolservers", Ct);
        var owners = Server(await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct), "team-a/hosted");

        Assert.Equal("https://mcp.hosted.example", Server(JsonDocument.Parse(list).RootElement, "team-a/hosted").GetProperty("url").GetString());
        Assert.DoesNotContain("k3y-in-path", list);
        Assert.Equal("https://mcp.hosted.example/s/k3y-in-path/mcp", owners.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("Bad_Name", "http://mcp.example/mcp", "Name and namespace")]
    [InlineData("docs", "ftp://mcp.example/mcp", "http or https")]
    [InlineData("docs", "file:///etc/passwd", "http or https")]
    [InlineData("docs", "not a url", "http or https")]
    [InlineData("docs", "https://bot:token@mcp.example/mcp", "user name or password")]
    [InlineData("docs", "https://mcp.example/mcp?api_key=s3cret", "in its query")]
    public async Task Create_InvalidInput_Is400WithAReason_AndNothingReachesKagent(string name, string url, string reason)
    {
        var response = await _owner.PostAsJsonAsync("/api/toolservers", Form(name, url), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
    }

    [Theory]
    [InlineData("Bad Header", "value", "letters, digits and hyphens")]
    [InlineData("Authorization", "multi-line", "one line")]
    [InlineData("Authorization", "empty", "enter its value")]
    [InlineData("Authorization", "bad-secret", "the Secret name")]
    [InlineData("Authorization", "duplicate", "named once")]
    public async Task Create_InvalidHeader_Is400_NeverQuotingTheValue(string header, string shape, string reason)
    {
        object[] headers = shape switch
        {
            "multi-line" => [ValueHeader(header, SecretValue + "\r\nX-Injected: 1")],
            "empty" => [ValueHeader(header, "")],
            "bad-secret" => [SecretHeader(header, "Not_A_Name", "token")],
            "duplicate" => [ValueHeader(header, SecretValue), ValueHeader(header.ToLowerInvariant(), SecretValue)],
            _ => [ValueHeader(header, SecretValue)]
        };

        var response = await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp", headers: headers), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, text);
        Assert.DoesNotContain("FAKE-SECRET", text);
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
    }

    /// <summary>A header value passes through to kagent and nowhere else: not the API, the page, the audit, the record or the log.</summary>
    [Fact]
    public async Task AHeaderValue_IsNeverReturnedLoggedAuditedOrStored()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp", headers: [ValueHeader("Authorization", SecretValue)]), Ct)).EnsureSuccessStatusCode();
        (await _owner.PutAsJsonAsync("/api/toolservers/team-a/docs", Form("docs", "http://docs.tools.svc:8080/mcp", headers: [ValueHeader("Authorization", SecretValue)]), Ct)).EnsureSuccessStatusCode();
        _kagent.DiscoveredTools["team-a/docs"] = [];

        var list = await _owner.GetStringAsync("/api/toolservers", Ct);
        var refresh = await (await _owner.PostAsync("/api/toolservers/team-a/docs/refresh", null, Ct)).Content.ReadAsStringAsync(Ct);
        var audit = await _owner.GetStringAsync("/api/identity/audit?action=toolserver.created", Ct);
        var updated = await _owner.GetStringAsync("/api/identity/audit?action=toolserver.updated", Ct);
        var page = await (await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword)).GetAsync("/ToolServers");
        string stored;
        await using (var connection = new NpgsqlConnection(_database))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("SELECT string_agg(url || ' ' || headers::text, ';') FROM tool_servers", connection);
            stored = (string)(await command.ExecuteScalarAsync(Ct))!;
        }
        string log;
        await using (var file = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            log = await new StreamReader(file).ReadToEndAsync(Ct);

        Assert.Equal(SecretValue, _kagent.SecretData.Values.Last().Value); // it did reach kagent
        Assert.Contains("Authorization", stored);
        Assert.Contains("toolserver.created", audit);
        Assert.Contains("toolserver.updated", updated);
        Assert.Contains("team-a/docs", log); // the log is written, and names the server
        foreach (var text in new[] { list, refresh, audit, updated, await page.Content.ReadAsStringAsync(Ct), stored, log })
            Assert.DoesNotContain("FAKE-SECRET", text);
    }

    [Fact]
    public async Task ASupervisor_ReadsAndRefreshes_ButChangesNothing()
    {
        (await _owner.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc:8080/mcp"), Ct)).EnsureSuccessStatusCode();
        var supervisor = await PersonAsync(_host, _owner, "sue@skanyxx.example", "supervisor");
        var creates = _kagent.CallsTo("POST", "/api/toolservers").Count();

        var list = await supervisor.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);
        var refresh = await supervisor.PostAsync("/api/toolservers/team-a/docs/refresh", null, Ct);
        var create = await supervisor.PostAsJsonAsync("/api/toolservers", Form("other", "http://other.tools.svc/mcp"), Ct);
        var edit = await supervisor.PutAsJsonAsync("/api/toolservers/team-a/docs", Form("docs", "http://evil.example/mcp"), Ct);
        var delete = await supervisor.DeleteAsync("/api/toolservers/team-a/docs?confirmInUse=true", Ct);

        Assert.False(list.GetProperty("canWrite").GetBoolean());
        Assert.False(Server(list, "team-a/docs").GetProperty("canEdit").GetBoolean());
        Assert.False(Server(list, "team-a/docs").GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(creates, _kagent.CallsTo("POST", "/api/toolservers").Count());
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers"));
    }

    [Theory]
    [InlineData("builder")]
    [InlineData("employee")]
    public async Task OtherRoles_Get403_AndNothingReachesKagent(string role)
    {
        var person = await PersonAsync(_host, _owner, $"{role}@skanyxx.example", role);
        var listsBefore = _kagent.CallsTo("GET", "/api/toolservers").Count();

        var responses = new[]
        {
            await person.GetAsync("/api/toolservers", Ct),
            await person.PostAsync("/api/toolservers/kagent/skanyxx-memory-seed/refresh", null, Ct),
            await person.PostAsJsonAsync("/api/toolservers", Form("docs", "http://docs.tools.svc/mcp"), Ct),
            await person.PutAsJsonAsync("/api/toolservers/kagent/skanyxx-memory-seed", Form("docs", "http://docs.tools.svc/mcp"), Ct),
            await person.DeleteAsync("/api/toolservers/kagent/skanyxx-memory-seed?confirmInUse=true", Ct)
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(listsBefore, _kagent.CallsTo("GET", "/api/toolservers").Count());
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers"));
    }

    [Fact]
    public async Task ThePage_IsTheOwnersAndSupervisors_AndOnlyTheOwnerSeesAdd()
    {
        await PersonAsync(_host, _owner, "sue@skanyxx.example", "supervisor");
        await PersonAsync(_host, _owner, "eli@skanyxx.example", "employee");

        var owner = await (await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword)).GetAsync("/ToolServers");
        var ownerHtml = await owner.Content.ReadAsStringAsync(Ct);
        var supervisorHtml = await (await (await SignInAsync("sue@skanyxx.example")).GetAsync("/ToolServers")).Content.ReadAsStringAsync(Ct);
        var employee = await SignInAsync("eli@skanyxx.example");
        var employeePage = await employee.GetAsync("/ToolServers");
        var employeeLibrary = await (await employee.GetAsync("/Library")).Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Contains("id=\"btnAddServer\"", ownerHtml);
        Assert.Contains("id=\"tsForm\"", ownerHtml);
        Assert.Contains("var isOwner = true;", ownerHtml);
        Assert.Contains("/js/setup-guide.js", ownerHtml);
        Assert.Contains("skxSetupGuide.render(fix", ownerHtml);
        Assert.Contains("href=\"/ToolServers\"", ownerHtml);
        Assert.Contains("not supported here", ownerHtml); // the stdio kind, shown and refused in the form
        Assert.DoesNotContain("innerHTML", ownerHtml); // kagent's and the servers' text is written with textContent only
        Assert.DoesNotContain("id=\"btnAddServer\"", supervisorHtml);
        Assert.DoesNotContain("id=\"tsForm\"", supervisorHtml);
        Assert.Contains("var isOwner = false;", supervisorHtml);
        Assert.Contains("href=\"/ToolServers\"", supervisorHtml);
        Assert.Equal(HttpStatusCode.Redirect, employeePage.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, employeePage.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain("href=\"/ToolServers\"", employeeLibrary);
    }

    [Fact]
    public async Task TheSharedGuideScript_WritesTextOnly_AndHasEveryCopyButton()
    {
        var script = await _host.Client().GetStringAsync("/js/setup-guide.js", Ct);

        Assert.Contains("Copy instructions for agent", script);
        Assert.Contains("g.agentPrompt", script);
        Assert.Contains("Fix it: local development", script);
        Assert.Contains("Fix it: Helm install", script);
        Assert.Contains("step.command", script);
        Assert.DoesNotContain("innerHTML", script);
    }

    /// <summary>The MVC controller (signed in was enough) is gone: api/toolservers is FastEndpoints only.</summary>
    [Fact]
    public void TheLegacyController_IsRemoved()
    {
        var endpoints = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/toolservers", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(5, endpoints.Count);
        Assert.All(endpoints, e => Assert.Null(e.Metadata.GetMetadata<ControllerActionDescriptor>()));
        Assert.All(endpoints, e => Assert.NotNull(e.Metadata.GetMetadata<FastEndpoints.EndpointDefinition>()));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetName().Name?.StartsWith("Skanyxx") == true ? a.GetTypes() : []),
            t => t.Name == "ToolServersController" || t.Name == "IToolServerService");
    }

    private async Task<Browser> SignInAsync(string email, string password = Password)
    {
        var browser = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password })).StatusCode);
        return browser;
    }

    internal static async Task<HttpClient> PersonAsync(HostApp host, HttpClient owner, string email, string role)
    {
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }
}
