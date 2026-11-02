using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Investigate in the whole Host (D340): the module loads from modules/, a run goes through <see cref="FakeKAgent"/>'s A2A,
/// the finding lands in memory as a card and the audit has the run without its text; the page and the API are for the
/// owner, supervisors and builders only.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class InvestigateTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Question = "INVESTIGATE-QUESTION-5d1e why are refunds late?";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
        });
        _owner = await _host.OwnerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    [Fact]
    public async Task ARun_AsksTheMergedAgent_LeavesAMemoryCard_AndAuditsWithoutItsText()
    {
        var options = await _owner.GetFromJsonAsync<JsonElement>("/api/investigate/options", Ct);
        var created = await _owner.PostAsJsonAsync("/api/investigate/workflows", new
        {
            name = "Refunds",
            steps = new object[]
            {
                new { id = "s1", title = "Ask", kind = "agent", agent = "kagent/seed", prompt = "Look into {{input}}", steps = Array.Empty<object>() },
                new { id = "g1", title = "Both", kind = "group", mode = "parallel", steps = new object[]
                {
                    new { id = "s2", title = "Again", kind = "agent", agent = "team-a/seed", prompt = "{{steps.s1}}", steps = Array.Empty<object>() },
                } },
            }
        }, Ct);
        var workflow = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var started = await _owner.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.GetProperty("id").GetString(), input = Question }, Ct);
        var runId = (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();

        JsonElement run;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            await Task.Delay(100, Ct);
            run = await _owner.GetFromJsonAsync<JsonElement>($"/api/investigate/runs/{runId}", Ct);
        } while (run.GetProperty("state").GetString() is "queued" or "running" && DateTime.UtcNow < deadline);

        Assert.Contains(options.GetProperty("agents").EnumerateArray(), a => a.GetProperty("ref").GetString() == "kagent/seed");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal("succeeded", run.GetProperty("state").GetString());
        Assert.All(run.GetProperty("stepRuns").EnumerateArray(), s => Assert.Equal("succeeded", s.GetProperty("state").GetString()));
        Assert.Equal(_kagent.Answer, run.GetProperty("stepRuns").EnumerateArray().Single(s => s.GetProperty("stepId").GetString() == "s2").GetProperty("output").GetString());
        Assert.Equal(2, _kagent.CallsTo("POST", "/api/a2a/").Count());

        var scope = run.GetProperty("cardScope").GetString();
        var key = run.GetProperty("cardKey").GetString();
        var card = await _owner.GetAsync($"/api/memory/cards/{scope}/{key}", Ct);
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        Assert.Contains("Investigation Refunds", await card.Content.ReadAsStringAsync(Ct));

        var audit = await _owner.GetStringAsync("/api/identity/audit?action=investigate.run_finished", Ct);
        Assert.Contains("succeeded", audit);
        Assert.DoesNotContain("INVESTIGATE-QUESTION-5d1e", audit);
        Assert.DoesNotContain("Look into", audit);

        var pdf = await _owner.GetAsync($"/api/investigate/runs/{runId}/report.pdf", Ct);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>D557: a step may call any agent Chat offers, labelled or not; never a Studio preview or a ticket stage.</summary>
    [Fact]
    public async Task AStep_TakesAnUnlabelledAgent_ButNeverAPreview()
    {
        _kagent.AgentObjects["kagent/preview-9-helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["name"] = "preview-9-helper", ["namespace"] = "kagent",
                ["labels"] = new JsonObject { ["skanyxx.dev/preview"] = "9", ["skanyxx.dev/managed-by"] = "skanyxx-studio" }
            },
            ["spec"] = new JsonObject { ["type"] = "Declarative" }
        };
        Task<HttpResponseMessage> SaveAsync(string agent) => _owner.PostAsJsonAsync("/api/investigate/workflows", new
        {
            name = $"Uses {agent}",
            steps = new object[] { new { id = "s1", title = "Ask", kind = "agent", agent, prompt = "{{input}}", steps = Array.Empty<object>() } }
        }, Ct);

        var options = await _owner.GetFromJsonAsync<JsonElement>("/api/investigate/options", Ct);
        var unlabelled = await SaveAsync("kagent/e2e-hello");
        var preview = await SaveAsync("kagent/preview-9-helper");
        var stage = await SaveAsync("kagent/ticket-planner");

        var refs = options.GetProperty("agents").EnumerateArray().Select(a => a.GetProperty("ref").GetString()).ToList();
        Assert.Contains("kagent/e2e-hello", refs);
        Assert.DoesNotContain("kagent/preview-9-helper", refs);
        Assert.DoesNotContain("kagent/ticket-planner", refs);
        Assert.Equal(HttpStatusCode.Created, unlabelled.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Contains("Not an agent you can use now", await preview.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, stage.StatusCode);
    }

    [Fact]
    public async Task ThePage_IsForBuildersSupervisorsAndTheOwner_NeverAnEmployee()
    {
        var employeeApi = await ToolServersApiTests.PersonAsync(_host, _owner, "eli@skanyxx.example", "employee");
        await ToolServersApiTests.PersonAsync(_host, _owner, "bea@skanyxx.example", "builder");

        var builder = await SignInAsync("bea@skanyxx.example");
        var builderPage = await builder.GetAsync("/Investigate");
        var builderHtml = await builderPage.Content.ReadAsStringAsync(Ct);
        var employee = await SignInAsync("eli@skanyxx.example");
        var employeePage = await employee.GetAsync("/Investigate");
        var employeeLibrary = await (await employee.GetAsync("/Library")).Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, builderPage.StatusCode);
        Assert.Contains("/js/investigate.js", builderHtml);
        Assert.Contains("href=\"/Investigate\"", builderHtml);
        Assert.Contains("data-supervisor=\"false\"", builderHtml);
        Assert.Equal(HttpStatusCode.Redirect, employeePage.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, employeePage.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain("href=\"/Investigate\"", employeeLibrary);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeApi.GetAsync("/api/investigate/workflows", Ct)).StatusCode);
        Assert.DoesNotContain("id=\"setup-investigate\"", await (await (await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword)).GetAsync("/Investigate")).Content.ReadAsStringAsync(Ct));
    }

    private async Task<Browser> SignInAsync(string email, string password = ToolServersApiTests.Password)
    {
        var browser = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password })).StatusCode);
        return browser;
    }
}
