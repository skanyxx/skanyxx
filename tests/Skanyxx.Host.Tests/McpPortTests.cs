using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// H3 (slice 4 QA): with Memory:McpPort set, /mcp/memory is served on that port only, so whatever publishes the main
/// port (the Helm chart's ingress) cannot publish /mcp with it, whatever its path rules say. On the main port the route
/// does not exist: the request gets what any unmapped path gets (the Host's sign-in fallback), never the MCP server.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class McpPortTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WithAnMcpPort_McpIsOnlyThere_AndTheRestStaysOnTheMainPort()
    {
        int main = FreePort(), mcpPort = FreePort();
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcpPort}";
            s["Memory:McpPort"] = mcpPort.ToString();
        });
        var owner = await host.OwnerAsync();
        var issued = await owner.PostAsync("/api/memory/agents/port-agent/secret", null, TestContext.Current.CancellationToken);
        issued.EnsureSuccessStatusCode();
        var secret = (await issued.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("secret").GetString()!;

        var onMain = await Client(main, secret).SendAsync(ToolsList(), TestContext.Current.CancellationToken);
        var onMcp = await Client(mcpPort, secret).SendAsync(ToolsList(), TestContext.Current.CancellationToken);
        // The Host header is the caller's to choose; the port that counts is the one the connection arrived on.
        var spoofed = ToolsList();
        spoofed.Headers.Host = $"localhost:{mcpPort}";
        var onMainSpoofed = await Client(main, secret).SendAsync(spoofed, TestContext.Current.CancellationToken);
        var getOnMain = await Client(main, secret).GetAsync("/mcp/memory", TestContext.Current.CancellationToken);
        // What the main port answers for a path nothing maps: the Host's sign-in fallback, which an agent secret fails.
        var unmapped = await Client(main, secret).PostAsync("/mcp/nothing-here", null, TestContext.Current.CancellationToken);
        var health = await Client(main).GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, unmapped.StatusCode);
        Assert.Equal(unmapped.StatusCode, onMain.StatusCode);
        Assert.Equal(unmapped.StatusCode, onMainSpoofed.StatusCode);
        Assert.Equal(unmapped.StatusCode, getOnMain.StatusCode);
        Assert.DoesNotContain("memory_search", await onMain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, onMcp.StatusCode);
        Assert.Contains("memory_search", await onMcp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/api/memory/agents")]
    [InlineData("/Setup")]
    [InlineData("/css/site.css")]
    [InlineData("/mcp")]
    [InlineData("/mcp/memoryx")]
    public async Task OnTheMcpPort_EveryOtherPath_Is404(string path)
    {
        int main = FreePort(), mcpPort = FreePort();
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcpPort}";
            s["Memory:McpPort"] = mcpPort.ToString();
        });

        var onMcp = await Client(mcpPort).GetAsync(path, TestContext.Current.CancellationToken);
        var postOnMcp = await Client(mcpPort).PostAsync(path, null, TestContext.Current.CancellationToken);
        var health = await Client(main).GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, onMcp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, postOnMcp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task AnMcpPortNothingListensOn_IsAWarning()
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Memory:McpPort"] = "1");
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains(console.ToString().Split('\n'), line => line.Contains("\"@l\":\"Warning\"") && line.Contains("Memory:McpPort is 1"));
    }

    [Fact]
    public async Task AnMcpPortTheServerListensOn_IsNoWarning()
    {
        int main = FreePort(), mcpPort = FreePort();
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
            {
                s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcpPort}";
                s["Memory:McpPort"] = mcpPort.ToString();
            });
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.DoesNotContain("Memory:McpPort is", console.ToString());
    }

    [Fact]
    public async Task OnTheMcpPort_ItStillNeedsAnAgentSecret()
    {
        int main = FreePort(), mcpPort = FreePort();
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcpPort}";
            s["Memory:McpPort"] = mcpPort.ToString();
        });

        var response = await Client(mcpPort).SendAsync(ToolsList(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HttpClient Client(int port, string? bearer = null)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        return client;
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
