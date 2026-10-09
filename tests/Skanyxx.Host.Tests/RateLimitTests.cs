namespace Skanyxx.Host.Tests;

/// <summary>SEC M5: one per-IP fixed window over the new-module APIs, chat, the model step and /mcp; other routes are not counted.</summary>
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
        // The key ring and the owner live in the shared database, so the fixture host's token is valid here too
        // (and obtaining it here would spend this host's window).
        var client = host.Client((await fixture.Host.OwnerAsync()).DefaultRequestHeaders.Authorization!.Parameter);

        var first = await client.GetAsync("/api/tickets/pipelines", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("/api/memory/cards?q=x", TestContext.Current.CancellationToken);
        var third = await client.GetAsync("/api/sandboxes/tasks", TestContext.Current.CancellationToken);
        var mcp = await client.PostAsync("/mcp/memory", new StringContent("{}"), TestContext.Current.CancellationToken);
        var chat = await client.PostAsync("/api/chat", new StringContent("{}"), TestContext.Current.CancellationToken);
        var model = await client.GetAsync("/api/model", TestContext.Current.CancellationToken);
        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var studioForm = await client.PostAsync("/Studio?handler=Factory", new StringContent(""), TestContext.Current.CancellationToken); // m11: the page's forms do what api/studio does
        var studioPage = await client.GetAsync("/Studio", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.TooManyRequests, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, chat.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, model.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, studioForm.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, studioPage.StatusCode);
    }
}
