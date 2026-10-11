using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D076/D077 on the real Host: the sandbox listener (Memory:SandboxMcpPort) serves /mcp/memory and nothing else, and
/// there a request is its sandbox token or a 401 — whatever headers or agent secret it carries. The token is worth
/// nothing on the UI/API port or the agents' mcp port. Sandboxes:MemoryMcpUrl is accepted for that listener only.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class SandboxListenerTests(PostgresFixture fixture)
{
    private const string Key = "a-sandbox-token-key-of-at-least-32-characters";
    private const string Owner = "0a0a0a0a-0000-4000-8000-000000000001";

    private static async Task<(HostApp Host, int Main, int Mcp, int Sandbox)> StartAsync(string database, Action<Dictionary<string, string>>? more = null)
    {
        int main = FreePort(), mcp = FreePort(), sandbox = FreePort();
        var host = await HostApp.StartAsync(database, s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcp};http://127.0.0.1:{sandbox}";
            s["Memory:McpPort"] = mcp.ToString();
            s["Memory:SandboxMcpPort"] = sandbox.ToString();
            s["Memory:SandboxTokenKey"] = Key;
            more?.Invoke(s);
        });
        return (host, main, mcp, sandbox);
    }

    private static async Task<string> IssueAsync(HostApp host, string agentId = "ax-fix-42-0a1b2c3d")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new IssueSandboxMemoryTokenCommand("fix-42", agentId, Owner), TestContext.Current.CancellationToken);
        Assert.Equal(OutcomeStatus.Ok, issued.Status);
        return issued.Value!.Token;
    }

    [Fact]
    public async Task OnTheSandboxListener_OnlyTheSandboxTokenCounts()
    {
        var (host, main, mcp, sandbox) = await StartAsync(await fixture.NewDatabaseAsync());
        await using var _ = host;
        var owner = await host.OwnerAsync();
        var issued = await owner.PostAsJsonAsync("/api/memory/agents/kagent-agent/secret", new { actsForUsers = true }, TestContext.Current.CancellationToken);
        var secret = (await issued.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("secret").GetString()!;
        var token = await IssueAsync(host);

        var withToken = await Send(sandbox, token);
        // Naming an agent and a user in headers, with no token or with an agent secret, gets nobody in.
        var headersOnly = await Send(sandbox, null, ("X-Agent-Id", "ax-fix-42-0a1b2c3d"), ("X-User-Id", Owner));
        var agentSecret = await Send(sandbox, secret, ("X-User-Id", Owner));
        var tokenOnMcp = await Send(mcp, token);
        var tokenOnMain = await Send(main, token);
        // The port that counts is the connection's, not the Host header the caller chose.
        var spoofed = await Send(main, token, ("Host", $"localhost:{sandbox}"));
        var secretOnMcp = await Send(mcp, secret);

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Contains("memory_search", await withToken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, headersOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, agentSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenOnMcp.StatusCode);
        var noSuchRoute = await Send(main, token, "/mcp/no-such-route");
        // With Memory:McpPort set, /mcp/memory does not exist on the main port (the matcher drops it): the token gets
        // exactly what a route that was never mapped gets there — the Host's fallback "sign in" 401 — not the MCP
        // route's own challenge, and certainly not a 500.
        Assert.Equal((HttpStatusCode.Unauthorized, "Sign in first."), await AnswerAsync(noSuchRoute));
        Assert.Equal((HttpStatusCode.Unauthorized, "Sign in first."), await AnswerAsync(tokenOnMain));
        Assert.Equal((HttpStatusCode.Unauthorized, "Sign in first."), await AnswerAsync(spoofed));
        Assert.Equal(HttpStatusCode.OK, secretOnMcp.StatusCode);
    }

    // Without Memory:McpPort, /mcp/memory is on the main port for agent secrets and carries no port metadata, so the
    // only thing keeping a sandbox token off it is the scheme's own port check.
    [Fact]
    public async Task WithoutAnMcpPort_TheTokenIsStillWorthNothingOnTheMainPort()
    {
        int main = FreePort(), sandbox = FreePort();
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{sandbox}";
            s["Memory:SandboxMcpPort"] = sandbox.ToString();
            s["Memory:SandboxTokenKey"] = Key;
        });
        var owner = await host.OwnerAsync();
        var issued = await owner.PostAsJsonAsync("/api/memory/agents/kagent-agent/secret", new { actsForUsers = true }, TestContext.Current.CancellationToken);
        var secret = (await issued.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("secret").GetString()!;
        var token = await IssueAsync(host);

        var tokenOnSandbox = await Send(sandbox, token);
        var tokenOnMain = await Send(main, token);
        var spoofed = await Send(main, token, ("Host", $"localhost:{sandbox}"));
        var secretOnMain = await Send(main, secret);
        var secretOnSandbox = await Send(sandbox, secret);

        Assert.Equal(HttpStatusCode.OK, tokenOnSandbox.StatusCode);
        // The route exists here, so its own challenge answers: the sandbox scheme stayed silent off its port.
        Assert.Equal((HttpStatusCode.Unauthorized, AgentSecretRequired), await AnswerAsync(tokenOnMain));
        Assert.Equal((HttpStatusCode.Unauthorized, AgentSecretRequired), await AnswerAsync(spoofed));
        Assert.Equal(HttpStatusCode.OK, secretOnMain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secretOnSandbox.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(sandbox, token).GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AStoppedSandboxesToken_IsRefusedAtOnce()
    {
        var (host, _, _, sandbox) = await StartAsync(await fixture.NewDatabaseAsync());
        await using var _ = host;
        var token = await IssueAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new RevokeSandboxMemoryTokenCommand("ax-fix-42-0a1b2c3d"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(sandbox, token)).StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/Login")]
    [InlineData("/Setup")]
    [InlineData("/Sandboxes")]
    [InlineData("/css/site.css")]
    [InlineData("/api/memory/agents")]
    [InlineData("/api/memory/grants/ax-fix-42-0a1b2c3d")]
    [InlineData("/api/sandboxes")]
    [InlineData("/api/identity/unlock")]
    [InlineData("/mcp")]
    [InlineData("/mcp/memoryx")]
    public async Task OnTheSandboxListener_EveryOtherPath_Is404(string path)
    {
        var (host, main, _, sandbox) = await StartAsync(fixture.ConnectionString);
        await using var _ = host;
        var token = await IssueAsync(host, $"ax-fix-42-{Random.Shared.Next(0x10000000, int.MaxValue):x8}");
        var client = Client(sandbox, token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(main).GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("mcp")]
    [InlineData("other")]
    public async Task MemoryMcpUrl_OnAnyOtherPort_StillStopsStartup(string target)
    {
        int main = FreePort(), mcp = FreePort(), sandbox = FreePort();
        var port = target switch { "main" => main, "mcp" => mcp, _ => 9 };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["urls"] = $"http://127.0.0.1:{main};http://127.0.0.1:{mcp};http://127.0.0.1:{sandbox}";
            s["Memory:McpPort"] = mcp.ToString();
            s["Memory:SandboxMcpPort"] = sandbox.ToString();
            s["Memory:SandboxTokenKey"] = Key;
            s["Sandboxes:MemoryMcpUrl"] = $"http://127.0.0.1:{port}/mcp/memory";
        }));

        Assert.Contains("D076/D077", ex.ToString());
    }

    [Fact]
    public async Task MemoryMcpUrl_OnTheSandboxListener_Starts()
    {
        var (host, _, _, sandbox) = await StartAsync(fixture.ConnectionString, s =>
            s["Sandboxes:MemoryMcpUrl"] = $"http://127.0.0.1:{s["Memory:SandboxMcpPort"]}/mcp/memory");
        await using var _ = host;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(sandbox, null)).StatusCode);
    }

    [Fact]
    public async Task ASandboxListenerNothingListensOn_IsAWarning()
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
            {
                s["Memory:SandboxMcpPort"] = "1";
                s["Memory:SandboxTokenKey"] = Key;
            });
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains(console.ToString().Split('\n'), line => line.Contains("\"@l\":\"Warning\"") && line.Contains("Memory:SandboxMcpPort is 1"));
    }

    private const string AgentSecretRequired = "An agent secret is required: Authorization: Bearer <secret>.";

    private static async Task<(HttpStatusCode, string?)> AnswerAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("detail").GetString());

    private static Task<HttpResponseMessage> Send(int port, string? bearer, params (string Name, string Value)[] headers) =>
        Send(port, bearer, "/mcp/memory", headers);

    private static Task<HttpResponseMessage> Send(int port, string? bearer, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        foreach (var (name, value) in headers)
        {
            if (name == "Host")
                request.Headers.Host = value;
            else
                request.Headers.Add(name, value);
        }
        return Client(port, bearer).SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpClient Client(int port, string? bearer = null)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        return client;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
