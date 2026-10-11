using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class SandboxApiTests : SandboxesTestBase
{
    [Fact]
    public async Task Run_CreatesTheSandboxInAx_OwnedByTheCaller_WithMemoryIdentity()
    {
        var response = await RunAsync("fix-42");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sandbox = (await response.Content.ReadFromJsonAsync<Sandbox>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal("fix-42", sandbox.Name);
        Assert.Equal(SandboxesApp.User, sandbox.Owner);
        Assert.Matches(@"^ax-fix-42-[0-9a-f]{8}\z", sandbox.AgentId);
        Assert.True(Skanyxx.Core.Platform.Identifier.IsValid(sandbox.AgentId));
        Assert.Equal("Pending", sandbox.Phase);

        var sent = Assert.Single(Ax.Updates).Task;
        Assert.Equal("ax.io/v1alpha1", sent.ApiVersion);
        Assert.Equal("Task", sent.Kind);
        Assert.Equal(SandboxesApp.Atespace, sent.Metadata.Atespace);
        Assert.Equal("ghcr.io/acme/agent@sha256:0123abcd", sent.Spec.Image);
        Assert.Equal(["my-agent", "--goal", "fix the flaky test"], sent.Spec.Command);
        Assert.Equal(new Dictionary<string, string>
        {
            ["RUN_ID"] = "42", ["SKANYXX_OWNER"] = "ana", ["SKANYXX_AGENT_ID"] = sandbox.AgentId!, ["SKANYXX_USER_ID"] = "ana"
        }, sent.Spec.Env.ToDictionary(e => e.Name, e => e.Value));
        Assert.Equal("repo-ws", sent.Spec.Workspaces[0].Name);
        Assert.Equal("/workspace/repo", sent.Spec.Workspaces[0].Path);
        Assert.Equal("500m", sent.Spec.Resources.Requests.Cpu);
        Assert.Equal("4Gi", sent.Spec.Resources.Limits.Memory);
        Assert.False(sent.Spec.Debug);
    }

    [Fact]
    public async Task Run_NeverReturnsEnvValues()
    {
        var body = await (await RunAsync("fix-42", body: RunBody(env: new Dictionary<string, string> { ["API_TOKEN"] = "s3cr3t-value" })))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var read = await App.Client().GetStringAsync("/api/sandboxes/fix-42", TestContext.Current.CancellationToken);

        Assert.Contains("API_TOKEN", read);
        Assert.DoesNotContain("s3cr3t-value", body);
        Assert.DoesNotContain("s3cr3t-value", read);
    }

    [Fact]
    public async Task Run_OnAnExistingSandbox_IsAnUpdate_KeepingOwnerSuspensionAndAgentId()
    {
        var first = await (await RunAsync("fix-42")).Content.ReadFromJsonAsync<Sandbox>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        await App.Client(SandboxesApp.Supervisor).PostAsync("/api/sandboxes/fix-42/suspend", null, TestContext.Current.CancellationToken);

        var response = await RunAsync("fix-42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Ax.Updates[^1].Task;
        Assert.Equal("ana", sent.Spec.Env.Single(e => e.Name == "SKANYXX_OWNER").Value);
        Assert.Equal(first!.AgentId, sent.Spec.Env.Single(e => e.Name == "SKANYXX_AGENT_ID").Value);
        Assert.True(sent.Spec.Suspend);
    }

    [Fact]
    public async Task Get_ReturnsTheSandbox_Or404()
    {
        Ax.Seed("seeded", "ana");

        var sandbox = await App.Client().GetFromJsonAsync<Sandbox>("/api/sandboxes/seeded", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await App.Client().GetAsync("/api/sandboxes/nope", TestContext.Current.CancellationToken);

        Assert.Equal("Running", sandbox!.Phase);
        Assert.Equal("ana", sandbox.Owner);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task List_PagesThroughAx_InTheConfiguredAtespace()
    {
        foreach (var name in new[] { "a", "b", "c" })
            Ax.Seed(name, "ana");

        var page = await App.Client().GetFromJsonAsync<List<Sandbox>>("/api/sandboxes?limit=2&offset=1", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["b", "c"], page!.Select(t => t.Name));
        Assert.All(Ax.Atespaces, a => Assert.Equal(SandboxesApp.Atespace, a));
    }

    // D179: the collection is /api/sandboxes itself, not /api/sandboxes/ and not a sub-path.
    [Fact]
    public async Task List_IsAtExactlyApiSandboxes()
    {
        Ax.Seed("a", "ana");

        var list = await App.Client().GetFromJsonAsync<List<Sandbox>>("/api/sandboxes", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("a", Assert.Single(list!).Name);
        Assert.Contains("GET api/sandboxes", Routes());
    }

    [Fact]
    public void Routes_UseTheSandboxNoun() =>
        Assert.Equal(
        [
            "GET api/sandboxes", "GET api/sandboxes/models", "GET api/sandboxes/workspaces", "GET api/sandboxes/{name}",
            "GET api/sandboxes/{name}/watch", "POST api/sandboxes/{name}/resume", "POST api/sandboxes/{name}/stop",
            "POST api/sandboxes/{name}/suspend", "PUT api/sandboxes/workspaces/{name}", "PUT api/sandboxes/{name}"
        ], Routes());

    // D179: the experimental /api/sandboxes/tasks/* routes are gone. GET /api/sandboxes/tasks is now a read of a
    // sandbox named "tasks" (none exists: 404); the rest match no route and never reach AX.
    [Theory]
    [InlineData("PUT", "/api/sandboxes/tasks/fix-42")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/stop")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/suspend")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/resume")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42/watch")]
    public async Task TheOldTaskRoutes_Are404(string method, string path)
    {
        await RunAsync("fix-42");
        var before = Ax.Atespaces.Count;

        var response = await App.Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, Ax.Atespaces.Count);
    }

    [Fact]
    public async Task TheOldListRoute_Is404()
    {
        await RunAsync("fix-42");

        var response = await App.Client().GetAsync("/api/sandboxes/tasks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("fix-42", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private List<string> Routes() =>
    [
        .. App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/sandboxes", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(m => $"{m} {e.RoutePattern.RawText!.TrimStart('/')}"))
            .Order(StringComparer.Ordinal)
    ];

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    public async Task List_RejectsBadPaging(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await App.Client().GetAsync($"/api/sandboxes?{query}", TestContext.Current.CancellationToken)).StatusCode);

    [Fact]
    public async Task Stop_DeletesInAx_AndIsAccepted()
    {
        await RunAsync("fix-42");

        var response = await App.Client().PostAsync("/api/sandboxes/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, Ax.DeleteCalls);
        Assert.Equal("Terminating", Ax.Stored("fix-42")!.Status.Phase);
    }

    [Fact]
    public async Task SuspendAndResume_FlipTheSandbox()
    {
        await RunAsync("fix-42");

        var suspended = await (await App.Client().PostAsync("/api/sandboxes/fix-42/suspend", null, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Sandbox>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var resumed = await (await App.Client().PostAsync("/api/sandboxes/fix-42/resume", null, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Sandbox>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(suspended!.Suspended);
        Assert.Equal("Suspended", suspended.Phase);
        Assert.False(resumed!.Suspended);
        Assert.Equal("Running", resumed.Phase);
    }

    [Theory]
    [InlineData("POST", "/api/sandboxes/nope/stop")]
    [InlineData("POST", "/api/sandboxes/nope/suspend")]
    [InlineData("POST", "/api/sandboxes/nope/resume")]
    [InlineData("GET", "/api/sandboxes/nope/watch")]
    public async Task MissingSandbox_Is404(string method, string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await App.Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken)).StatusCode);

    [Fact]
    public async Task Models_AreListed_WithoutTheirSecretRef()
    {
        Ax.SeedModel("planner");

        var body = await App.Client().GetStringAsync("/api/sandboxes/models", TestContext.Current.CancellationToken);
        var models = System.Text.Json.JsonSerializer.Deserialize<List<SandboxModel>>(body, SandboxesApp.Json);

        Assert.Equal(new SandboxModel("planner", "google", "gemini-2.5-pro"), Assert.Single(models!));
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KEY", body);
    }

    // SEC L1: AX condition messages come from controller/Substrate errors and are never returned.
    [Fact]
    public async Task Conditions_AreReturnedWithoutTheirMessage()
    {
        Ax.Seed("seeded", "ana", phase: "Failed").Status.Conditions.Add(new Ax.V1Alpha1.Condition
        {
            Type = "Ready", Status = "False", Reason = "ActorCreationFailed", Message = FakeAx.SecretDetail
        });

        var body = await App.Client().GetStringAsync("/api/sandboxes/seeded", TestContext.Current.CancellationToken);
        var list = await App.Client().GetStringAsync("/api/sandboxes", TestContext.Current.CancellationToken);

        Assert.Contains("ActorCreationFailed", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("hunter2", list);
        Assert.DoesNotContain("\"message\"", body);
    }
}
