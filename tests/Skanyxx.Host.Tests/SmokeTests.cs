using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>The composed Host: every module loaded by the real Program.cs, one route per new module, as the signed-in owner.</summary>
[Collection(HostCollection.Name)]
public sealed class SmokeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Health_IsHealthy_WithEveryDatabaseCheck()
    {
        var response = await fixture.Host.Client().GetAsync("/health", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        var entries = body.RootElement.GetProperty("entries").EnumerateObject().Select(e => e.Name).ToList();
        Assert.Contains("memory-postgres", entries);
        Assert.Contains("tickets-postgres", entries);
        Assert.Contains("identity-postgres", entries);
    }

    [Fact]
    public async Task Me_IsTheOwner()
    {
        var me = await (await fixture.Host.OwnerAsync()).GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HostApp.OwnerEmail, me.GetProperty("email").GetString());
        Assert.Equal(["owner"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task MemorySearch_Ok()
    {
        var response = await (await fixture.Host.OwnerAsync()).GetAsync("/api/memory/cards?q=deploy", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TicketPipelines_Ok()
    {
        var response = await (await fixture.Host.OwnerAsync()).GetAsync("/api/tickets/pipelines", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SandboxTasks_AxUnreachable_Is502()
    {
        var response = await (await fixture.Host.OwnerAsync()).GetAsync("/api/sandboxes/tasks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task SandboxesDisabled_HostStarts_AndItsRoutesAre404()
    {
        var owner = await fixture.Host.OwnerAsync();
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s.Remove("Sandboxes:Enabled"));

        // Same database, so the same key ring and owner: the fixture host's token works here.
        var response = await host.Client(owner.DefaultRequestHeaders.Authorization!.Parameter).GetAsync("/api/sandboxes/tasks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Development turns on ValidateOnBuild, which resolves every MediatR handler — the Sandboxes ones included.
    [Fact]
    public async Task SandboxesDisabled_HostStartsInDevelopment()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s =>
        {
            s["environment"] = "Development";
            s.Remove("Sandboxes:Enabled");
        });

        var response = await host.Client().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
