using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D541: with Argo CD syncing main's <c>agents/*/agent.yaml</c>, Argo can create an Agent before the reconciler does (a
/// restore, or a later sync re-creating an agent the owner suspended). Such an Agent carries Argo's tracking id for the
/// studio's Application and no Skanyxx label: the reconciler adopts it (labelled, Argo's tracking kept) and keeps a
/// suspended one out of kagent. Anything else unlabelled — another Application's, or a direct agent — is still refused.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class StudioArgoAdoptionTests(PostgresFixture fixture) : IAsyncLifetime
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
    public async Task AnAgentArgoCreatedFromMain_IsAdopted_LabelledAndStillTrackedByArgo()
    {
        await ApplyAsync("refund-helper");
        ArgoRecreates("refund-helper", Tracking("skanyxx-agents", "refund-helper"));

        Assert.Equal(0, await ReconcileAsync());

        var agent = _kagent.AgentObjects[$"{Ns}/refund-helper"];
        Assert.Equal("skanyxx-studio", agent["metadata"]!["labels"]!["skanyxx.dev/managed-by"]!.GetValue<string>());
        Assert.Equal("true", agent["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
        Assert.Equal(Tracking("skanyxx-agents", "refund-helper"), agent["metadata"]!["annotations"]![ArgoAnnotation]!.GetValue<string>());
        Assert.Equal(0, await ReconcileAsync()); // and then it is simply ours: idempotent
    }

    [Theory]
    [InlineData("another-app", null)]
    [InlineData("skanyxx-agents", "skanyxx-agents")]
    public async Task AnUnlabelledAgent_ArgoDidNotCreateForTheStudio_IsStillRefused(string application, string? managedBy)
    {
        await ApplyAsync("refund-helper");
        ArgoRecreates("refund-helper", Tracking(application, "refund-helper"), managedBy);

        Assert.Equal(1, await ReconcileAsync());

        Assert.Null(_kagent.AgentObjects[$"{Ns}/refund-helper"]["metadata"]!["labels"]?["skanyxx.dev/merged"]);
    }

    [Fact]
    public async Task ASuspendedAgent_ArgoPutBack_IsTakenOutAgain_AndAdoptedOnResume()
    {
        await ApplyAsync("refund-helper");
        Assert.Contains((await _owner.PostAsync("/api/studio/agents/refund-helper/suspend", null, Ct)).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));

        ArgoRecreates("refund-helper", Tracking("skanyxx-agents", "refund-helper"));
        Assert.Equal(0, await ReconcileAsync());
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));

        ArgoRecreates("refund-helper", Tracking("skanyxx-agents", "refund-helper"));
        Assert.Contains((await _owner.PostAsync("/api/studio/agents/refund-helper/resume", null, Ct)).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });
        // The resume wakes the reconciler; its pass adopts what Argo put back.
        await WaitUntilAsync(() => _kagent.AgentObjects.TryGetValue($"{Ns}/refund-helper", out var a)
            && a["metadata"]?["labels"]?["skanyxx.dev/merged"]?.GetValue<string>() == "true", "the adopted agent after the resume");
    }

    /// <summary>D545: a folder the studio refuses that Argo applied anyway (a push to main) does not stay running.</summary>
    [Fact]
    public async Task AnInvalidFolderArgoApplied_IsRemoved_WhileAValidAgentWithAnInvalidEditKeepsRunning()
    {
        await ApplyAsync("refund-helper");
        _git.Main()["agents/evil/agent.yaml"] = "apiVersion: kagent.dev/v1alpha2\nkind: Agent\n";
        _git.Main()["agents/evil/grants.yaml"] = "agent: evil\ngrants: []\n";
        ArgoRecreates("evil", Tracking("skanyxx-agents", "evil"));
        _git.Main()["agents/refund-helper/agent.yaml"] = "kind: ConfigMap\n";

        Assert.Equal(0, await ReconcileAsync());

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/evil"));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    private const string ArgoAnnotation = "argocd.argoproj.io/tracking-id";

    private static string Tracking(string application, string agent) => $"{application}:kagent.dev/Agent:{Ns}/{agent}";

    /// <summary>What an Argo sync leaves in kagent: main's file, Argo's annotation, no Skanyxx label (or another's).</summary>
    private void ArgoRecreates(string agent, string tracking, string? managedBy = null)
    {
        var applied = _kagent.AgentObjects.TryGetValue($"{Ns}/{agent}", out var existing)
            ? (JsonObject)existing.DeepClone()
            : _argoFile!.DeepClone().AsObject();
        applied["metadata"] = new JsonObject
        {
            ["name"] = agent,
            ["namespace"] = Ns,
            ["annotations"] = new JsonObject { [ArgoAnnotation] = tracking },
            ["labels"] = managedBy is null ? null : new JsonObject { ["skanyxx.dev/managed-by"] = managedBy }
        };
        _argoFile = applied.DeepClone().AsObject();
        _kagent.AgentObjects[$"{Ns}/{agent}"] = applied;
    }

    private JsonObject? _argoFile;

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
        _argoFile = _kagent.AgentObjects[$"{Ns}/{name}"].DeepClone().AsObject();
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
