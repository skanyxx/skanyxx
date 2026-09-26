namespace Skanyxx.Host.Tests;

/// <summary>DNS rebinding (SEC H1): a foreign Host is refused by host filtering, a foreign Origin by the origin guard.</summary>
[Collection(HostCollection.Name)]
public sealed class RebindingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ForgedHost_Is400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tickets/pipelines");
        request.Headers.Host = "rebind.attacker.example";

        var response = await fixture.Host.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/memory/cards?q=x")]
    [InlineData("GET", "/api/tickets/pipelines")]
    [InlineData("GET", "/api/sandboxes/tasks")]
    [InlineData("POST", "/mcp/memory")]
    public async Task ForeignOrigin_Is403Problem(string method, string path)
    {
        var response = await SendAsync(method, path, "http://rebind.attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AllowedOrigin_Passes()
    {
        var response = await SendAsync("GET", "/api/tickets/pipelines", HostApp.AllowedOrigin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NoOrigin_ReachesMcp()
    {
        var response = await SendAsync("POST", "/mcp/memory", origin: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOrigin_OnLegacyPage_IsNotGuarded()
    {
        var response = await SendAsync("GET", "/Privacy", "http://rebind.attacker.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WildcardAllowedHosts_OutsideDevelopment_RefusesToStart()
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        InvalidOperationException ex;
        try
        {
            ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                HostApp.StartAsync(fixture.ConnectionString, s => s["AllowedHosts"] = "*"));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("AllowedHosts must list the host names", ex.Message);
        // CR m8: a startup failure is logged as Fatal (and flushed) before the process exits.
        Assert.Contains(console.ToString().Split('\n'), line => line.Contains("\"@l\":\"Fatal\"") && line.Contains("AllowedHosts"));
    }

    private Task<HttpResponseMessage> SendAsync(string method, string path, string? origin)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json");
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        return fixture.Host.Client().SendAsync(request);
    }
}
