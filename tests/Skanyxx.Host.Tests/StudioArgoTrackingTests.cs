using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D546: Argo CD only reports on studio agents. With <c>Studio:Argo</c> the reconciler stamps each merged agent with the
/// tracking id of the studio's Application (so Argo compares it with main) and stays its only writer: an unlabelled
/// Agent Argo's id points at is refused like any foreign one, never taken over.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class StudioArgoTrackingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Ns = "kagent";
    private const string Password = "a long member passphrase";

    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private FakeArgo _argo = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;

    public async ValueTask InitializeAsync()
    {
        _git = await FakeGitea.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _argo = await FakeArgo.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:Argo:BaseUrl"] = _argo.BaseUrl;
            s["Studio:Argo:Token"] = FakeArgo.Token;
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
            HostApp.WithoutSandboxes(s);
        });
        _owner = await _host.OwnerAsync();
        await WaitUntilAsync(() => _git.Protections.Count == 1, "the first reconcile pass after setup");
        _supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _argo.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
    }

    [Fact]
    public async Task AMergedAgent_CarriesTheApplicationsTrackingId()
    {
        await ApplyAsync("refund-helper");

        Assert.Equal(Tracking("skanyxx-agents", "refund-helper"), Annotation(_kagent.AgentObjects[$"{Ns}/refund-helper"]));
        Assert.Null(Annotation(_kagent.AgentObjects[$"{Ns}/skanyxx-factory"]));
        Assert.Equal(0, await ReconcileAsync());
    }

    [Fact]
    public async Task AnAgentMergedBeforeArgo_IsRecreatedOnceWithTheId()
    {
        await ApplyAsync("refund-helper");
        _kagent.AgentObjects[$"{Ns}/refund-helper"]["metadata"]!.AsObject().Remove("annotations");

        Assert.Equal(0, await ReconcileAsync());

        var agent = _kagent.AgentObjects[$"{Ns}/refund-helper"];
        Assert.Equal(Tracking("skanyxx-agents", "refund-helper"), Annotation(agent));
        Assert.Equal("true", agent["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnlabelledAgentArgosIdPointsAt_IsRefused_NotTakenOver()
    {
        await ApplyAsync("refund-helper");
        var argoMade = _kagent.AgentObjects[$"{Ns}/refund-helper"].DeepClone().AsObject();
        argoMade["metadata"] = new JsonObject
        {
            ["name"] = "refund-helper", ["namespace"] = Ns,
            ["annotations"] = new JsonObject { [ArgoAnnotation] = Tracking("skanyxx-agents", "refund-helper") }
        };
        _kagent.AgentObjects[$"{Ns}/refund-helper"] = argoMade;

        Assert.Equal(1, await ReconcileAsync());

        Assert.Null(_kagent.AgentObjects[$"{Ns}/refund-helper"]["metadata"]!["labels"]);
    }

    private const string ArgoAnnotation = "argocd.argoproj.io/tracking-id";

    private static string? Annotation(JsonObject agent) => agent["metadata"]?["annotations"]?[ArgoAnnotation]?.GetValue<string>();

    private static string Tracking(string application, string agent) => $"{application}:kagent.dev/Agent:{Ns}/{agent}";

    private async Task ApplyAsync(string name)
    {
        var applied = await _supervisor.PostAsJsonAsync("/api/studio/apply", new
        {
            name,
            description = "Answers refund questions",
            modelConfig = "default-model-config",
            instructions = "Answer refund questions from company memory.",
            skills = Array.Empty<string>(),
            mcpTools = Array.Empty<object>(),
            grants = new[] { new { scope = "company", search = true, upsert = false } },
            memoryTtlDays = (int?)null
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
    }

    private async Task<int> ReconcileAsync()
    {
        var loop = _host.Services.GetServices<IHostedService>().Single(h => h.GetType().Name == "StudioReconcileLoop");
        var type = loop.GetType().Assembly.GetType("Skanyxx.Module.Agents.Studio.Reconcile.StudioReconciler")!;
        var reconciler = _host.Services.GetRequiredService(type);
        return await (Task<int>)type.GetMethod("ReconcileAsync")!.Invoke(reconciler, [CancellationToken.None])!;
    }

    private async Task<HttpClient> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return _host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }

    private static async Task WaitUntilAsync(Func<bool> done, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Waited {seconds} s for {what}.");
            await Task.Delay(50);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
