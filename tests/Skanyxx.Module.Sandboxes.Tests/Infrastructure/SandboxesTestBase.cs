using System.Net.Http.Json;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

public abstract class SandboxesTestBase : IAsyncLifetime
{
    protected FakeAxServer Server { get; private set; } = null!;
    protected FakeAx Ax => Server.Ax;
    protected SandboxesApp App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Server = await FakeAxServer.StartAsync();
        App = await SandboxesApp.StartAsync(Server.Url);
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Server.DisposeAsync();
    }

    /// <summary>A second app on the same fake AX with changed settings (a null value removes a default); dispose it.</summary>
    protected Task<SandboxesApp> StartAppAsync(Action<Dictionary<string, string?>> configure) =>
        SandboxesApp.StartAsync(Server.Url, configure);

    protected static object RunBody(object? env = null, object? workspaces = null) => new
    {
        image = "ghcr.io/acme/agent@sha256:0123abcd",
        command = new[] { "my-agent", "--goal", "fix the flaky test" },
        env = env ?? new Dictionary<string, string> { ["RUN_ID"] = "42" },
        workspaces = workspaces ?? new[] { new { name = "repo-ws", path = "/workspace/repo" } },
        resources = new { requests = new { cpu = "500m", memory = "1Gi" }, limits = new { cpu = "2", memory = "4Gi" } }
    };

    protected Task<HttpResponseMessage> RunAsync(string name, string? userId = SandboxesApp.User, object? body = null, SandboxesApp? app = null) =>
        (app ?? App).Client(userId).PutAsJsonAsync($"/api/sandboxes/tasks/{name}", body ?? RunBody());
}
