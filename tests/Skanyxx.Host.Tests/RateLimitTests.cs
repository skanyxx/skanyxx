namespace Skanyxx.Host.Tests;

/// <summary>SEC M5: one per-IP fixed window over the header-identity APIs; other routes are not counted.</summary>
[Collection(HostCollection.Name)]
public sealed class RateLimitTests(PostgresFixture fixture)
{
    [Fact]
    public async Task OverTheLimit_Is429Problem_OnlyOnGuardedRoutes()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["Skanyxx:RateLimit:PermitLimit"] = "2";
            s["Skanyxx:RateLimit:WindowSeconds"] = "600";
        });
        var client = host.Client();

        var first = await client.GetAsync("/api/tickets/pipelines");
        var second = await client.GetAsync("/api/memory/cards?q=x");
        var third = await client.GetAsync("/api/sandboxes/tasks");
        var mcp = await client.PostAsync("/mcp/memory", new StringContent("{}"));
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.TooManyRequests, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
