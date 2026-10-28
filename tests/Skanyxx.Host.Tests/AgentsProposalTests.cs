using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D312 on the real host against <see cref="FakeGitea"/> and <see cref="FakeKAgent"/>: on the Agents page, builders and
/// employees submit the agent form as a Studio pull request and never change a live agent; what a studio agent cannot
/// hold is refused at its field; a change keeps the agent's memory grants; the git token never shows.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AgentsProposalTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";

    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private HttpClient _employee = null!;
    private StringWriter _console = null!;
    private TextWriter _originalOut = null!;

    public async ValueTask InitializeAsync()
    {
        _originalOut = Console.Out;
        _console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(_console));
        _git = await FakeGitea.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
        });
        _owner = await _host.OwnerAsync();
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"), "the first reconcile pass after setup");
        _supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");
        _builder = await PersonAsync("bo@skanyxx.example", "builder");
        _employee = await PersonAsync("eve@skanyxx.example", "employee");
    }

    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_originalOut);
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuildersAndEmployees_GetAPullRequest_NeverALiveAgent()
    {
        var writes = KAgentAgentWrites();

        var fromBuilder = await _builder.PostAsJsonAsync("/api/agents/proposals", Form("refund-helper"), Ct);
        var fromEmployee = await _employee.PostAsJsonAsync("/api/agents/proposals", Form("travel-helper"), Ct);

        Assert.Equal(HttpStatusCode.Created, fromBuilder.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromEmployee.StatusCode);
        var number = (await fromEmployee.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32();
        var pull = _git.Pulls.Single(p => p.Number == number);
        Assert.True(pull.Open);
        Assert.Contains("travel-helper", _git.Repos["skanyxx/skanyxx-agents"][pull.Head]["agents/travel-helper/agent.yaml"]);
        Assert.Equal(2, _git.Pulls.Count);
        Assert.Equal(writes, KAgentAgentWrites());
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper") || _kagent.AgentObjects.ContainsKey($"{Ns}/travel-helper"));
        Assert.DoesNotContain(FakeGitea.Token, _console.ToString());
        Assert.DoesNotContain(FakeGitea.Token, await fromBuilder.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task SupervisorsAndTheOwner_SaveDirectly_AndAreRefusedAProposal()
    {
        foreach (var client in new[] { _owner, _supervisor })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/agents/proposals", Form("refund-helper"), Ct)).StatusCode);
        Assert.Empty(_git.Pulls);
    }

    [Fact]
    public async Task WhatAStudioAgentCannotHold_IsRefusedAtItsField_BeforeGit()
    {
        var form = Form("refund-helper");
        form["agentTools"] = new[] { "e2e-hello" };
        form["a2aSkills"] = new[] { new { id = "x", name = "X" } };
        form["stream"] = true;
        form["memory"] = new { modelConfig = "other-model", ttlDays = 7 };

        var response = await _builder.PostAsJsonAsync("/api/agents/proposals", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        foreach (var field in new[] { "agentTools", "a2aSkills", "runtime", "memory" })
            Assert.True(errors.TryGetProperty(field, out _), $"no problem under '{field}': {errors}");
        Assert.Empty(_git.Pulls);
    }

    [Fact]
    public async Task AChangeToAStudioAgent_KeepsItsMemoryGrants_AndAForeignAgentIsRefused()
    {
        var first = await _builder.PostAsJsonAsync("/api/studio/proposals", new
        {
            name = "refund-helper", description = "Answers refund questions", modelConfig = "default-model-config",
            instructions = "Answer refund questions.", skills = Array.Empty<string>(), mcpTools = Array.Empty<object>(),
            grants = new[] { new { scope = "company", search = true, upsert = false } }, memoryTtlDays = (int?)null
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var number = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);

        var change = Form("refund-helper");
        change["description"] = "Answers refund questions politely.";
        var response = await _builder.PostAsJsonAsync("/api/agents/proposals", change, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pull = _git.Pulls.Single(p => p.Open);
        Assert.Contains("- scope: company\n  search: true\n  upsert: false", _git.Repos["skanyxx/skanyxx-agents"][pull.Head]["agents/refund-helper/grants.yaml"]);

        // e2e-hello runs in kagent and is not the studio's: a proposal never takes it over.
        var foreign = await _employee.PostAsJsonAsync("/api/agents/proposals", Form("e2e-hello"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        Assert.Single(_git.Pulls, p => p.Open);
    }

    private static Dictionary<string, object?> Form(string name) => new()
    {
        ["namespace"] = Ns,
        ["name"] = name,
        ["description"] = "Answers questions.",
        ["systemMessage"] = "Answer briefly and cite the policy.",
        ["modelConfig"] = "default-model-config",
        ["runtime"] = null,
        ["stream"] = false,
        ["mcpTools"] = Array.Empty<object>(),
        ["agentTools"] = Array.Empty<string>(),
        ["skills"] = Array.Empty<string>(),
        ["a2aSkills"] = Array.Empty<object>()
    };

    private int KAgentAgentWrites() =>
        _kagent.Calls.Count(c => c.Method is "POST" or "PUT" or "DELETE" && c.Path.StartsWith("/api/agents", StringComparison.Ordinal));

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
}
