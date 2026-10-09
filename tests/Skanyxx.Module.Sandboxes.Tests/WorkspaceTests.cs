using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class WorkspaceTests : SandboxesTestBase
{
    private Task<HttpResponseMessage> SaveAsync(string name, object body, string user = SandboxesApp.Supervisor) =>
        App.Client(user).PutAsJsonAsync($"/api/sandboxes/workspaces/{name}", body);

    [Fact]
    public async Task Save_SendsGitAndMcpServers_ToAx()
    {
        var response = await SaveAsync("repo-ws", new
        {
            git = new[] { new { repo = "https://github.com/acme/app.git", branch = "main", depth = 1 } },
            mcpServers = new[] { new { name = "docs", endpoint = "https://docs.example/mcp" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SandboxWorkspace>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.False(saved.MemoryAttached);

        var sent = Assert.Single(Ax.WorkspaceUpdates).Workspace;
        Assert.Equal(SandboxesApp.Atespace, sent.Metadata.Atespace);
        Assert.Equal("Workspace", sent.Kind);
        Assert.Equal([("docs", "https://docs.example/mcp")], sent.Spec.Mcp.Servers.Select(s => (s.Name, s.Endpoint)));
        Assert.All(sent.Spec.Mcp.Servers, s => Assert.Empty(s.Command));
        Assert.Equal("https://github.com/acme/app.git", sent.Spec.Git[0].Repo);
        Assert.Equal(1, sent.Spec.Git[0].Depth);
    }

    // The attach path is kept for D076/D077, but no configuration reaches it today (MemoryMcpUrl must be empty).
    [Fact]
    public void AttachMemory_AddsTheSkanyxxMemoryServer_WhenAUrlIsGiven()
    {
        const string memoryUrl = "http://skanyxx.skanyxx.svc:8080/mcp/memory";

        var workspace = AxMapper.ToAxWorkspace("repo-ws", [], [new McpServerEntry("docs", "https://docs.example/mcp")], memoryUrl);

        Assert.Equal([("docs", "https://docs.example/mcp"), ("skanyxx-memory", memoryUrl)],
            workspace.Spec.Mcp.Servers.Select(s => (s.Name, s.Endpoint)));
        Assert.True(AxMapper.ToSandboxWorkspace(workspace).MemoryAttached);
    }

    [Fact]
    public async Task WithoutAttachMemory_NoMemoryServer()
    {
        await SaveAsync("plain", new { git = Array.Empty<object>() });

        var listed = await App.Client().GetFromJsonAsync<List<SandboxWorkspace>>("/api/sandboxes/workspaces", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(listed!).MemoryAttached);
        Assert.Empty(Assert.Single(Ax.WorkspaceUpdates).Workspace.Spec.Mcp.Servers);
    }

    [Fact]
    public async Task OnlySupervisors_WriteWorkspaces()
    {
        var response = await SaveAsync("repo-ws", new { git = Array.Empty<object>() }, SandboxesApp.User);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(Ax.WorkspaceUpdates);
    }

    public static TheoryData<string, object> BadWorkspaces => new()
    {
        { "credentials in repo url", new { git = new[] { new { repo = "https://user:token@github.com/acme/app.git" } } } },
        { "plain http repo", new { git = new[] { new { repo = "http://github.com/acme/app.git" } } } },
        { "file repo", new { git = new[] { new { repo = "file:///etc/passwd" } } } },
        { "dir escape", new { git = new[] { new { repo = "https://github.com/a/b.git", dir = "../../etc" } } } },
        { "reserved mcp name", new { mcpServers = new[] { new { name = "skanyxx-memory", endpoint = "https://evil.example/mcp" } } } },
        { "mcp not a url", new { mcpServers = new[] { new { name = "docs", endpoint = "npx something" } } } }
    };

    [Theory]
    [MemberData(nameof(BadWorkspaces))]
    public async Task BadWorkspaces_Are400(string why, object body)
    {
        var response = await SaveAsync("repo-ws", body);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, why);
        Assert.Empty(Ax.WorkspaceUpdates);
    }

    [Fact]
    public async Task AttachMemory_Is400_WhileNoMemoryUrlCanBeConfigured()
    {
        var response = await SaveAsync("ws", new { attachMemory = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Ax.WorkspaceUpdates);
    }
}
