using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class TaskApiTests : SandboxesTestBase
{
    [Fact]
    public async Task Run_CreatesTheAxTask_OwnedByTheCaller_WithMemoryIdentity()
    {
        var response = await RunAsync("fix-42");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal("fix-42", task.Name);
        Assert.Equal(SandboxesApp.User, task.Owner);
        Assert.Matches(@"^ax-fix-42-[0-9a-f]{8}\z", task.AgentId);
        Assert.True(Skanyxx.Core.Platform.Identifier.IsValid(task.AgentId));
        Assert.Equal("Pending", task.Phase);

        var sent = Assert.Single(Ax.Updates).Task;
        Assert.Equal("ax.io/v1alpha1", sent.ApiVersion);
        Assert.Equal("Task", sent.Kind);
        Assert.Equal(SandboxesApp.Atespace, sent.Metadata.Atespace);
        Assert.Equal("ghcr.io/acme/agent@sha256:0123abcd", sent.Spec.Image);
        Assert.Equal(["my-agent", "--goal", "fix the flaky test"], sent.Spec.Command);
        Assert.Equal(new Dictionary<string, string>
        {
            ["RUN_ID"] = "42", ["SKANYXX_OWNER"] = "ana", ["SKANYXX_AGENT_ID"] = task.AgentId!, ["SKANYXX_USER_ID"] = "ana"
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
        var read = await App.Client().GetStringAsync("/api/sandboxes/tasks/fix-42", TestContext.Current.CancellationToken);

        Assert.Contains("API_TOKEN", read);
        Assert.DoesNotContain("s3cr3t-value", body);
        Assert.DoesNotContain("s3cr3t-value", read);
    }

    [Fact]
    public async Task Run_OnAnExistingTask_IsAnUpdate_KeepingOwnerSuspensionAndAgentId()
    {
        var first = await (await RunAsync("fix-42")).Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        await App.Client(SandboxesApp.Supervisor).PostAsync("/api/sandboxes/tasks/fix-42/suspend", null, TestContext.Current.CancellationToken);

        var response = await RunAsync("fix-42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Ax.Updates[^1].Task;
        Assert.Equal("ana", sent.Spec.Env.Single(e => e.Name == "SKANYXX_OWNER").Value);
        Assert.Equal(first!.AgentId, sent.Spec.Env.Single(e => e.Name == "SKANYXX_AGENT_ID").Value);
        Assert.True(sent.Spec.Suspend);
    }

    [Fact]
    public async Task Get_ReturnsTheTask_Or404()
    {
        Ax.Seed("seeded", "ana");

        var task = await App.Client().GetFromJsonAsync<SandboxTask>("/api/sandboxes/tasks/seeded", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await App.Client().GetAsync("/api/sandboxes/tasks/nope", TestContext.Current.CancellationToken);

        Assert.Equal("Running", task!.Phase);
        Assert.Equal("ana", task.Owner);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task List_PagesThroughAx_InTheConfiguredAtespace()
    {
        foreach (var name in new[] { "a", "b", "c" })
            Ax.Seed(name, "ana");

        var page = await App.Client().GetFromJsonAsync<List<SandboxTask>>("/api/sandboxes/tasks?limit=2&offset=1", SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["b", "c"], page!.Select(t => t.Name));
        Assert.All(Ax.Atespaces, a => Assert.Equal(SandboxesApp.Atespace, a));
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    public async Task List_RejectsBadPaging(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await App.Client().GetAsync($"/api/sandboxes/tasks?{query}", TestContext.Current.CancellationToken)).StatusCode);

    [Fact]
    public async Task Stop_DeletesInAx_AndIsAccepted()
    {
        await RunAsync("fix-42");

        var response = await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, Ax.DeleteCalls);
        Assert.Equal("Terminating", Ax.Stored("fix-42")!.Status.Phase);
    }

    [Fact]
    public async Task SuspendAndResume_FlipTheTask()
    {
        await RunAsync("fix-42");

        var suspended = await (await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/suspend", null, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var resumed = await (await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/resume", null, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(suspended!.Suspended);
        Assert.Equal("Suspended", suspended.Phase);
        Assert.False(resumed!.Suspended);
        Assert.Equal("Running", resumed.Phase);
    }

    [Theory]
    [InlineData("POST", "/api/sandboxes/tasks/nope/stop")]
    [InlineData("POST", "/api/sandboxes/tasks/nope/suspend")]
    [InlineData("POST", "/api/sandboxes/tasks/nope/resume")]
    [InlineData("GET", "/api/sandboxes/tasks/nope/watch")]
    public async Task MissingTask_Is404(string method, string path) =>
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

        var body = await App.Client().GetStringAsync("/api/sandboxes/tasks/seeded", TestContext.Current.CancellationToken);
        var list = await App.Client().GetStringAsync("/api/sandboxes/tasks", TestContext.Current.CancellationToken);

        Assert.Contains("ActorCreationFailed", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("hunter2", list);
        Assert.DoesNotContain("\"message\"", body);
    }
}
