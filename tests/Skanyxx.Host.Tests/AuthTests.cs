using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The fallback policy in the composed Host: APIs answer an anonymous caller with 401 ProblemDetails, pages redirect
/// to the login page, and only the listed routes stay anonymous. A plain <c>X-User-Id</c> header authenticates nothing.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AuthTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("/api/memory/cards?q=x")]
    [InlineData("/api/memory/grants/some-agent")]
    [InlineData("/api/tickets/pipelines")]
    [InlineData("/api/tickets/runs")]
    [InlineData("/api/sandboxes/tasks")]
    [InlineData("/api/identity/me")]
    [InlineData("/api/hooks")] // a legacy controller
    [InlineData("/api/no-such-route")]
    public async Task Anonymous_Api_Is401Problem(string path)
    {
        var response = await fixture.Host.Client().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/memory/cards?q=x")]
    [InlineData("/api/tickets/pipelines")]
    [InlineData("/api/sandboxes/tasks")]
    public async Task UserIdHeader_Alone_Is401(string path)
    {
        var client = fixture.Host.Client();
        client.DefaultRequestHeaders.Add("X-User-Id", "ana");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_ReachesALegacyController()
    {
        var response = await (await fixture.Host.OwnerAsync()).GetAsync("/api/hooks");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/Hooks", "/Login?ReturnUrl=%2FHooks")]
    [InlineData("/", "/Login?ReturnUrl=%2F")]
    public async Task Anonymous_Page_RedirectsToLogin(string path, string location)
    {
        var response = await fixture.Host.Client().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, new Uri(fixture.Host.BaseAddress, response.Headers.Location!).PathAndQuery);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/css/site.css")]
    [InlineData("/Privacy")]
    [InlineData("/Login")]
    [InlineData("/api/identity/status")]
    public async Task Anonymous_Allowed(string path)
    {
        await fixture.Host.OwnerAsync(); // bootstrapped, so /Login shows the form instead of redirecting to setup

        var response = await fixture.Host.Client().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // D080: no user sign-in on /mcp/memory, but an agent secret issued by a supervisor, and nothing else, gets in.
    [Fact]
    public async Task Mcp_AnswersAnAgentSecret_AndNothingElse()
    {
        var owner = await fixture.Host.OwnerAsync();
        var issued = await owner.PostAsync("/api/memory/agents/smoke-agent/secret", null);
        issued.EnsureSuccessStatusCode();
        var secret = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        var withSecret = await fixture.Host.Client(secret).SendAsync(ToolsList());
        var withAgentHeader = fixture.Host.Client();
        withAgentHeader.DefaultRequestHeaders.Add("X-Agent-Id", "smoke-agent");
        var headerOnly = await withAgentHeader.SendAsync(ToolsList());
        var userToken = await owner.SendAsync(ToolsList());

        Assert.Equal(HttpStatusCode.OK, withSecret.StatusCode);
        Assert.Contains("memory_search", await withSecret.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, headerOnly.StatusCode);
        Assert.Equal("Bearer", Assert.Single(headerOnly.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(HttpStatusCode.Unauthorized, userToken.StatusCode);
    }

    // /mcp is forwarded to the cookie handler by the Host's default scheme, so the endpoint's own scheme is all that
    // keeps a signed-in browser out: the session cookie alone is 401 there.
    [Fact]
    public async Task SignedInCookie_Alone_OnMcp_Is401()
    {
        await fixture.Host.OwnerAsync();
        var browser = new Browser(fixture.Host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });
        Assert.True(browser.HasCookie("skanyxx.auth"));

        var response = await browser.SendAsync(ToolsList());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    // The Host's default scheme sends a Bearer to the user token handler, which cannot read an agent secret.
    [Theory]
    [InlineData("/api/memory/cards?q=x")]
    [InlineData("/api/memory/agents/smoke-agent/secret")]
    [InlineData("/api/identity/me")]
    public async Task AgentSecret_OnTheRestApi_Is401(string path)
    {
        var secret = await IssueAsync(await fixture.Host.OwnerAsync(), "smoke-agent", actsForUsers: false);

        var response = await fixture.Host.Client(secret).GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync());
    }

    // D084: a browser session plus a valid secret on /mcp is the agent, and the named user counts only when the owner
    // let that agent act for users. The signed-in owner is never the caller.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignedInCookie_PlusSecret_OnMcp_IsTheAgent_WithTheUserOnlyWhenActingForUsers(bool actsForUsers)
    {
        var owner = await fixture.Host.OwnerAsync();
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/identity/me")).GetProperty("id").GetString()!;
        var agent = actsForUsers ? "acting-agent" : "plain-agent";
        var secret = await IssueAsync(owner, agent, actsForUsers);
        var browser = new Browser(fixture.Host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });
        Assert.True(browser.HasCookie("skanyxx.auth"));

        var request = UpsertPersonal($"cookie-{agent}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        request.Headers.Add("X-User-Id", ownerId);
        var response = await browser.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (actsForUsers)
            Assert.Contains($"{ownerId} via {agent}", body);
        else
            Assert.Contains("No user context", body);
    }

    private static async Task<string> IssueAsync(HttpClient owner, string agentId, bool actsForUsers)
    {
        var issued = await owner.PostAsJsonAsync($"/api/memory/agents/{agentId}/secret", new { actsForUsers });
        issued.EnsureSuccessStatusCode();
        return (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
    }

    private static HttpRequestMessage UpsertPersonal(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/memory")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = 1, method = "tools/call",
                    @params = new { name = "memory_upsert", arguments = new { key, type = "fact", what = "w", why = "y", version = 0 } }
                }), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static HttpRequestMessage ToolsList()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/memory")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}
