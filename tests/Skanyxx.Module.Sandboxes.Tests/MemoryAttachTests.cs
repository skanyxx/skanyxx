using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

/// <summary>
/// D073/D076/D077 from the sandboxes side: with <c>Sandboxes:MemoryMcpUrl</c> on the sandbox listener, a run that binds a
/// workspace with memory attached gets the listener URL and a token memory signed for its agent id in its env — never an
/// agent secret — and stopping or re-running the sandbox revokes the previous token.
/// </summary>
public sealed class MemoryAttachTests : SandboxesTestBase
{
    private readonly FakeMemoryTokens _memory = new();

    private Task<SandboxesApp> MemoryAppAsync() =>
        SandboxesApp.StartAsync(Server.Url, FakeMemoryTokens.Configure, _memory.Register);

    private static object Bind(string workspace) => RunBody(workspaces: new[] { new { name = workspace, path = "/workspace/repo" } });

    private static Dictionary<string, string> Env(Ax.V1Alpha1.UpdateTaskRequest update) =>
        update.Task.Spec.Env.ToDictionary(e => e.Name, e => e.Value);

    [Fact]
    public async Task ARunBindingAMemoryWorkspace_GetsTheListenerUrlAndItsOwnToken()
    {
        Ax.SeedWorkspace("repo-ws", FakeMemoryTokens.Url);
        await using var app = await MemoryAppAsync();

        var response = await RunAsync("fix-42", app: app, body: Bind("repo-ws"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var issued = Assert.Single(_memory.Issued);
        var env = Env(Assert.Single(Ax.Updates));
        Assert.Equal(("fix-42", SandboxesApp.User), (issued.SandboxName, issued.OwnerId));
        Assert.Equal(env["SKANYXX_AGENT_ID"], issued.AgentId);
        Assert.Equal(FakeMemoryTokens.Url, env["SKANYXX_MEMORY_URL"]);
        Assert.Equal(FakeMemoryTokens.TokenFor(issued.AgentId, 1), env["SKANYXX_MEMORY_TOKEN"]);
        // D076: never an agent secret, whatever else is in the env.
        Assert.DoesNotContain(env.Values, v => v.Contains("skx_mem_"));
        // Env values are never returned, the token included.
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("SKANYXX_MEMORY_TOKEN", body);
        Assert.DoesNotContain("skx_sbx_", body);
        Assert.DoesNotContain(app.Logs.Entries, e => e.Message.Contains("skx_sbx_"));
    }

    [Theory]
    [InlineData("plain-ws")]
    [InlineData("missing-ws")]
    public async Task ARunWithoutAMemoryWorkspace_GetsNoTokenAtAll(string workspace)
    {
        Ax.SeedWorkspace("plain-ws", null);
        await using var app = await MemoryAppAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await RunAsync("fix-42", app: app, body: Bind(workspace))).StatusCode);

        Assert.Empty(_memory.Issued);
        var env = Env(Assert.Single(Ax.Updates));
        Assert.DoesNotContain("SKANYXX_MEMORY_TOKEN", env.Keys);
        Assert.DoesNotContain("SKANYXX_MEMORY_URL", env.Keys);
    }

    [Fact]
    public async Task ACallerCannotSetTheTokenEnv()
    {
        Ax.SeedWorkspace("repo-ws", FakeMemoryTokens.Url);
        await using var app = await MemoryAppAsync();

        var response = await RunAsync("fix-42", app: app, body: RunBody(
            env: new Dictionary<string, string> { ["SKANYXX_MEMORY_TOKEN"] = "skx_sbx_forged" },
            workspaces: new[] { new { name = "repo-ws", path = "/w" } }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Ax.Updates);
        Assert.Empty(_memory.Issued);
    }

    [Fact]
    public async Task ReplacingAnActiveSandbox_ReissuesForTheSameAgentId()
    {
        Ax.SeedWorkspace("repo-ws", FakeMemoryTokens.Url);
        await using var app = await MemoryAppAsync();
        // The fake AX keeps the first run Pending: still active, so the owner's second run replaces it.
        await RunAsync("fix-42", app: app, body: Bind("repo-ws"));

        Assert.Equal(HttpStatusCode.OK, (await RunAsync("fix-42", app: app, body: Bind("repo-ws"))).StatusCode);

        var issued = _memory.Issued.ToArray();
        Assert.Equal(2, issued.Length);
        Assert.Equal(issued[0].AgentId, issued[1].AgentId);
        Assert.Equal(FakeMemoryTokens.TokenFor(issued[1].AgentId, 2), Env(Ax.Updates[1])["SKANYXX_MEMORY_TOKEN"]);
        // The same row is replaced by the re-issue; nothing to revoke separately.
        Assert.Empty(_memory.Revoked);
    }

    [Fact]
    public async Task ReRunningAFinishedSandbox_RevokesTheOldIdentity_AndIssuesANewOne()
    {
        Ax.SeedWorkspace("repo-ws", FakeMemoryTokens.Url);
        await using var app = await MemoryAppAsync();
        var finished = Ax.Seed("fix-42", SandboxesApp.User, phase: "Completed");
        finished.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = "ax-fix-42-00000000" });

        Assert.Equal(HttpStatusCode.OK, (await RunAsync("fix-42", app: app, body: Bind("repo-ws"))).StatusCode);

        Assert.Equal(["ax-fix-42-00000000"], _memory.Revoked);
        Assert.NotEqual("ax-fix-42-00000000", Assert.Single(_memory.Issued).AgentId);
    }

    [Fact]
    public async Task ReplacingWithoutTheMemoryWorkspace_RevokesTheToken()
    {
        Ax.SeedWorkspace("plain-ws", null);
        await using var app = await MemoryAppAsync();
        var active = Ax.Seed("fix-42", SandboxesApp.User);
        active.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = "ax-fix-42-11111111" });

        Assert.Equal(HttpStatusCode.OK, (await RunAsync("fix-42", app: app, body: Bind("plain-ws"))).StatusCode);

        Assert.Equal(["ax-fix-42-11111111"], _memory.Revoked);
        Assert.Empty(_memory.Issued);
        Assert.Equal("ax-fix-42-11111111", Env(Assert.Single(Ax.Updates))["SKANYXX_AGENT_ID"]);
    }

    [Fact]
    public async Task WhenMemoryRefusesTheToken_TheRunFails_AndAxIsNotTold()
    {
        Ax.SeedWorkspace("repo-ws", FakeMemoryTokens.Url);
        _memory.IssueStatus = OutcomeStatus.Unavailable;
        await using var app = await MemoryAppAsync();

        var response = await RunAsync("fix-42", app: app, body: Bind("repo-ws"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode); // Unavailable, as for AX itself
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task Stop_RevokesTheToken_BeforeAxDeletes()
    {
        await using var app = await MemoryAppAsync();
        var running = Ax.Seed("fix-42", SandboxesApp.User);
        running.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = "ax-fix-42-22222222" });

        _memory.RevokeFails = true;
        var failed = await app.Client().PostAsync("/api/sandboxes/fix-42/stop", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode); // memory's Unavailable, not a 500
        Assert.Contains("memory is down", await failed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, Ax.DeleteCalls);

        _memory.RevokeFails = false;
        var stopped = await app.Client().PostAsync("/api/sandboxes/fix-42/stop", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, stopped.StatusCode);
        Assert.Equal(["ax-fix-42-22222222"], _memory.Revoked);
        Assert.Equal(1, Ax.DeleteCalls);
    }

    [Theory]
    [InlineData("not-from-skanyxx")]
    [InlineData("AX-UPPER-case")]
    [InlineData("ax-fix 42")]
    public async Task Stop_OfASandboxWhoseAgentIdNeverHadAToken_RevokesNothing_AndStops(string agentId)
    {
        await using var app = await MemoryAppAsync();
        var adopted = Ax.Seed("fix-42", SandboxesApp.User);
        adopted.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = agentId });

        var stopped = await app.Client().PostAsync("/api/sandboxes/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, stopped.StatusCode);
        Assert.Empty(_memory.Revoked);
        Assert.Equal(1, Ax.DeleteCalls);
    }

    private Ax.V1Alpha1.Task SeedSuspendedWithMemory(string agentId)
    {
        var suspended = Ax.Seed("fix-42", SandboxesApp.User, phase: "Suspended");
        suspended.Spec.Suspend = true;
        suspended.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = agentId });
        suspended.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_MEMORY_URL", Value = FakeMemoryTokens.Url });
        suspended.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_MEMORY_TOKEN", Value = "skx_sbx_expired-one" });
        return suspended;
    }

    [Fact]
    public async Task Resume_RenewsTheToken_ForTheSameAgentId_BeforeAxResumes()
    {
        await using var app = await MemoryAppAsync();
        SeedSuspendedWithMemory("ax-fix-42-33333333");

        var resumed = await app.Client().PostAsync("/api/sandboxes/fix-42/resume", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var issued = Assert.Single(_memory.Issued);
        Assert.Equal(("fix-42", "ax-fix-42-33333333", SandboxesApp.User), (issued.SandboxName, issued.AgentId, issued.OwnerId));
        var update = Assert.Single(Ax.Updates);
        Assert.True(update.Task.Spec.Suspend); // saved while still suspended; AX's resume wakes it with the new token
        Assert.Equal(FakeMemoryTokens.TokenFor("ax-fix-42-33333333", 1), Env(update)["SKANYXX_MEMORY_TOKEN"]);
        Assert.Equal("ax-fix-42-33333333", Env(update)["SKANYXX_AGENT_ID"]);
        var stored = Ax.Stored("fix-42")!;
        Assert.Equal("Running", stored.Status.Phase);
        Assert.Equal(FakeMemoryTokens.TokenFor("ax-fix-42-33333333", 1), stored.Spec.Env.Single(e => e.Name == "SKANYXX_MEMORY_TOKEN").Value);
        Assert.DoesNotContain("skx_sbx_", await resumed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resume_WhenMemoryRefusesTheRenewal_StaysSuspended()
    {
        _memory.IssueStatus = OutcomeStatus.Unavailable;
        await using var app = await MemoryAppAsync();
        SeedSuspendedWithMemory("ax-fix-42-44444444");

        var resumed = await app.Client().PostAsync("/api/sandboxes/fix-42/resume", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, resumed.StatusCode);
        Assert.Empty(Ax.Updates);
        Assert.Equal("Suspended", Ax.Stored("fix-42")!.Status.Phase);
    }

    [Fact]
    public async Task Resume_OfASandboxWithoutMemory_IssuesNothing()
    {
        await using var app = await MemoryAppAsync();
        var suspended = Ax.Seed("fix-42", SandboxesApp.User, phase: "Suspended");
        suspended.Spec.Suspend = true;
        suspended.Spec.Env.Add(new Ax.V1Alpha1.EnvVar { Name = "SKANYXX_AGENT_ID", Value = "ax-fix-42-55555555" });

        var resumed = await app.Client().PostAsync("/api/sandboxes/fix-42/resume", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Empty(_memory.Issued);
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task MemoryOff_IssuesAndRevokesNothing()
    {
        Ax.SeedWorkspace("repo-ws", "http://somewhere/mcp/memory");
        await using var app = await SandboxesApp.StartAsync(Server.Url, services: _memory.Register);
        await RunAsync("fix-42", app: app, body: Bind("repo-ws"));
        await app.Client().PostAsync("/api/sandboxes/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Empty(_memory.Issued);
        Assert.Empty(_memory.Revoked);
        Assert.DoesNotContain("SKANYXX_MEMORY_TOKEN", Env(Assert.Single(Ax.Updates)).Keys);
    }

    [Fact]
    public async Task AttachMemory_PutsTheListenerUrlOnTheWorkspace()
    {
        await using var app = await MemoryAppAsync();

        var response = await app.Client(SandboxesApp.Supervisor).PutAsJsonAsync("/api/sandboxes/workspaces/repo-ws", new { attachMemory = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<SandboxWorkspace>(SandboxesApp.Json, TestContext.Current.CancellationToken))!.MemoryAttached);
        Assert.Equal([("skanyxx-memory", FakeMemoryTokens.Url)],
            Assert.Single(Ax.WorkspaceUpdates).Workspace.Spec.Mcp.Servers.Select(s => (s.Name, s.Endpoint)));
    }
}
