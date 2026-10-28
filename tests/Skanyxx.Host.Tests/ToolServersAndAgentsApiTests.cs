using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The Tool Servers and Agents pages' APIs against <see cref="FakeKAgent"/> (D313, Chrome walkthrough 2026-10-28):
/// a tool server is created in kagent's own shape, and one agent's details come from <c>namespace/name</c>.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ToolServersAndAgentsApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
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

    [Fact]
    public async Task CreateToolServer_SendsKagentsRemoteMcpServerShape_AndDeleteRemovesIt()
    {
        var created = await _owner.PostAsJsonAsync("/api/toolservers", new { name = "crud-test-ts", @namespace = "team-a", url = "http://mcp.example:8080/mcp" }, Ct);
        var duplicate = await _owner.PostAsJsonAsync("/api/toolservers", new { name = "crud-test-ts", @namespace = "team-a", url = "http://mcp.example:8080/mcp" }, Ct);
        var listed = await _owner.GetFromJsonAsync<JsonElement>("/api/toolservers", Ct);
        var stored = _kagent.ToolServers["team-a/crud-test-ts"];
        var deleted = await _owner.DeleteAsync("/api/toolservers/team-a/crud-test-ts", Ct);
        var again = await _owner.DeleteAsync("/api/toolservers/team-a/crud-test-ts", Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("team-a/crud-test-ts", (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("ref").GetString());
        Assert.Equal("crud-test-ts", stored["metadata"]!["name"]!.GetValue<string>());
        Assert.Equal("team-a", stored["metadata"]!["namespace"]!.GetValue<string>());
        Assert.Equal("http://mcp.example:8080/mcp", stored["spec"]!["url"]!.GetValue<string>());
        Assert.Equal("STREAMABLE_HTTP", stored["spec"]!["protocol"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(listed.EnumerateArray(), s => s.GetProperty("ref").GetString() == "team-a/crud-test-ts");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(_kagent.ToolServers.ContainsKey("team-a/crud-test-ts"));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Theory]
    [InlineData("Bad_Name", "kagent", "http://mcp.example/mcp", "Name and namespace")]
    [InlineData("crud-test-ts", "kagent", "ftp://mcp.example/mcp", "http or https")]
    [InlineData("crud-test-ts", "kagent", "not a url", "http or https")]
    [InlineData(null, "kagent", "http://mcp.example/mcp", "Name and namespace")]
    [InlineData("crud-test-ts", "Bad/Ns", "http://mcp.example/mcp", "Name and namespace")]
    [InlineData("crud-test-ts", "kagent", "https://bot:token@mcp.example/mcp", "user name or password")]
    public async Task CreateToolServer_InvalidInput_Is400WithAReason_AndNothingIsCreated(string? name, string ns, string url, string reason)
    {
        var response = await _owner.PostAsJsonAsync("/api/toolservers", new { name, @namespace = ns, url }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
        Assert.Empty(_kagent.ToolServers);
    }

    [Fact]
    public async Task CreateToolServer_KagentFailing_Is502WithAFixedText()
    {
        _kagent.FailToolServerCreates = 1;

        var response = await _owner.PostAsJsonAsync("/api/toolservers", new { name = "crud-test-ts", @namespace = "kagent", url = "http://mcp.example/mcp" }, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("restarting", await response.Content.ReadAsStringAsync(Ct)); // kagent's text stays in the log
    }

    [Fact]
    public async Task ToolServersPage_ShowsAddAndDeleteToTheOwnerOnly()
    {
        var employee = await PersonAsync("eli@skanyxx.example", "employee");

        var owners = await _owner.GetStringAsync("/ToolServers", Ct);
        var employees = await employee.GetStringAsync("/ToolServers", Ct);

        Assert.Contains("id=\"btnAddServer\"", owners);
        Assert.Contains("var isOwner = true;", owners);
        Assert.DoesNotContain("id=\"btnAddServer\"", employees);
        Assert.Contains("var isOwner = false;", employees);
    }

    [Fact]
    public async Task AgentDetails_ByNamespaceAndName_ForAnyoneSignedIn_UnknownIs404()
    {
        var employee = await PersonAsync("eli@skanyxx.example", "employee");

        var seed = await employee.GetFromJsonAsync<JsonElement>("/api/agents/team-a/seed", Ct);
        var missing = await employee.GetAsync("/api/agents/kagent/no-such-agent", Ct);

        Assert.Equal("seed", seed.GetProperty("name").GetString());
        Assert.Equal("team-a", seed.GetProperty("namespace").GetString());
        Assert.Equal("true", seed.GetProperty("labels").GetProperty("skanyxx.dev/merged").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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
}
