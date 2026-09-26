using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

/// <summary>SEC H2: the module is off unless <c>Sandboxes:Enabled</c> is true, and off it serves nothing.</summary>
public sealed class EnabledTests : SandboxesTestBase
{
    [Theory]
    [InlineData("GET", "/api/sandboxes/tasks")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42")]
    [InlineData("PUT", "/api/sandboxes/tasks/fix-42")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/stop")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42/watch")]
    [InlineData("GET", "/api/sandboxes/models")]
    [InlineData("GET", "/api/sandboxes/workspaces")]
    [InlineData("PUT", "/api/sandboxes/workspaces/ws")]
    public async Task NotEnabled_EveryRouteIs404_AndAxIsNeverCalled(string method, string path)
    {
        Ax.Seed("fix-42", SandboxesApp.User, phase: "Running");
        // Settings that would stop an enabled module from starting are ignored while it is off.
        await using var app = await StartAppAsync(s =>
        {
            s["Sandboxes:Enabled"] = null;
            s["Sandboxes:NetworkIsolationConfirmed"] = null;
            s["Sandboxes:MemoryMcpUrl"] = "http://skanyxx/mcp/memory";
        });

        var response = await app.Client(SandboxesApp.Supervisor)
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(RunBody()) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Ax.Atespaces);
    }

    [Fact]
    public async Task Enabled_ServesTheRoutes() =>
        Assert.Equal(HttpStatusCode.OK, (await App.Client().GetAsync("/api/sandboxes/tasks")).StatusCode);
}
