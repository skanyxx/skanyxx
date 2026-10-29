using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>
/// D341 on the server: roles on every endpoint, then ownership. Bea (builder, billing) and Ben (builder, sales); Sam is a
/// supervisor in billing, Olga the owner, Eli an employee.
/// </summary>
public sealed class AccessTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private static readonly WorkflowStep[] OneStep = [Agent("s1", Researcher, "{{input}}")];

    private static object Body(string name = "W", string? sharedTeam = null) => new { name, description = "", sharedTeam, steps = OneStep };

    private async Task<HttpResponseMessage[]> EveryEndpointAsync(HttpClient client, Guid workflow, Guid run) =>
    [
        await client.GetAsync("/api/investigate/options", Ct),
        await client.GetAsync("/api/investigate/workflows", Ct),
        await client.GetAsync($"/api/investigate/workflows/{workflow}", Ct),
        await client.PostAsJsonAsync("/api/investigate/workflows", Body(), Json, Ct),
        await client.PutAsJsonAsync($"/api/investigate/workflows/{workflow}", Body(), Json, Ct),
        await client.PostAsync($"/api/investigate/workflows/{workflow}/duplicate", null, Ct),
        await client.DeleteAsync($"/api/investigate/workflows/{workflow}", Ct),
        await client.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow, input = "x" }, Ct),
        await client.GetAsync("/api/investigate/runs", Ct),
        await client.GetAsync($"/api/investigate/runs/{run}", Ct),
        await client.PostAsync($"/api/investigate/runs/{run}/cancel", null, Ct),
        await client.PostAsync($"/api/investigate/runs/{run}/rerun", null, Ct),
        await client.GetAsync($"/api/investigate/runs/{run}/report.json", Ct),
        await client.GetAsync($"/api/investigate/runs/{run}/report.pdf", Ct),
    ];

    private async Task<(WorkflowDto Workflow, RunDto Run)> BeasAsync(string? sharedTeam = null)
    {
        var workflow = await App.CreateAsync(Builder, "Bea's", OneStep, sharedTeam);
        var run = await App.StartAsync(Builder, workflow.Id, "secret question");
        await App.WaitForAsync(run.Id, RunState.Succeeded);
        return (workflow, run);
    }

    [Fact]
    public async Task AnEmployee_Gets403_OnEveryEndpoint_AndAnonymous401()
    {
        var (workflow, run) = await BeasAsync();
        var calls = App.Agents.Calls.Count;

        var employee = await EveryEndpointAsync(App.Client(Employee), workflow.Id, run.Id);
        var anonymous = await EveryEndpointAsync(App.Client(null), workflow.Id, run.Id);

        Assert.Equal(14, employee.Length);
        Assert.All(employee, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.All(anonymous, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(calls, App.Agents.Calls.Count);
        Assert.Equal(RunState.Succeeded, (await App.GetRunAsync(run.Id)).State);
    }

    [Fact]
    public async Task ABuilder_CannotSeeOrTouch_AnotherBuildersWorkflowOrRun()
    {
        var (workflow, run) = await BeasAsync();
        var ben = App.Client(OtherBuilder);

        var list = await ben.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        var runs = await ben.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        var responses = (await EveryEndpointAsync(ben, workflow.Id, run.Id))[2..];

        Assert.Empty(list!);
        Assert.Empty(runs!);
        // get, (create is his own: 201), update, duplicate, delete, start, (list: 200), get run, cancel, rerun, reports
        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.Created, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
             HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
             HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            responses.Select(r => r.StatusCode));
        Assert.Equal("Bea's", (await App.Client(Builder).GetFromJsonAsync<WorkflowDto>($"/api/investigate/workflows/{workflow.Id}", Json, Ct))!.Name);
    }

    [Fact]
    public async Task ATeamMember_SeesRunsAndDuplicates_ASharedWorkflow_ButCannotChangeOrDeleteIt()
    {
        var workflow = await App.CreateAsync(Builder, "Billing check", OneStep, "billing");
        var sam = App.Client(Supervisor);
        var beaWorkflows = await App.Client(Builder).GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        await App.Client(Owner).PutAsJsonAsync($"/api/investigate/workflows/{workflow.Id}", Body("Billing check", "sales"), Json, Ct);
        var benSees = await App.Client(OtherBuilder).GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        var ben = App.Client(OtherBuilder);

        var get = await ben.GetAsync($"/api/investigate/workflows/{workflow.Id}", Ct);
        var started = await ben.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.Id, input = "x" }, Ct);
        var copy = await ben.PostAsync($"/api/investigate/workflows/{workflow.Id}/duplicate", null, Ct);
        var edit = await ben.PutAsJsonAsync($"/api/investigate/workflows/{workflow.Id}", Body("Mine now", "sales"), Json, Ct);
        var delete = await ben.DeleteAsync($"/api/investigate/workflows/{workflow.Id}", Ct);
        var copied = (await copy.Content.ReadFromJsonAsync<WorkflowDto>(Json, Ct))!;

        Assert.True(Assert.Single(beaWorkflows!).Mine);
        Assert.False(Assert.Single(benSees!).CanEdit);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        Assert.Equal((OtherBuilder, (string?)null, "Copy of Billing check"), (copied.OwnerId, copied.SharedTeam, copied.Name));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync($"/api/investigate/workflows/{workflow.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task ABuilder_SharesOnlyWithTheirOwnTeams()
    {
        var own = await App.Client(Builder).PostAsJsonAsync("/api/investigate/workflows", Body("Ok", "billing"), Json, Ct);
        var other = await App.Client(Builder).PostAsJsonAsync("/api/investigate/workflows", Body("No", "sales"), Json, Ct);
        var options = await App.Client(Builder).GetFromJsonAsync<OptionsDto>("/api/investigate/options", Json, Ct);
        var supervisorOptions = await App.Client(Supervisor).GetFromJsonAsync<OptionsDto>("/api/investigate/options", Json, Ct);

        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(["billing"], options!.Teams.Select(t => t.Slug));
        Assert.Equal(["billing", "sales"], supervisorOptions!.Teams.Select(t => t.Slug));
        Assert.Equal([Researcher, Analyst, Writer], options.Agents.Select(a => a.Ref));
    }

    [Fact]
    public async Task TheOwnerAndSupervisors_SeeEditAndDeleteEveryonesWork()
    {
        var (workflow, run) = await BeasAsync();
        var sam = App.Client(Supervisor);

        var all = await sam.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        var mine = await sam.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs?mine=true", Json, Ct);
        var report = await sam.GetAsync($"/api/investigate/runs/{run.Id}/report.json", Ct);
        var edit = await sam.PutAsJsonAsync($"/api/investigate/workflows/{workflow.Id}", Body("Renamed by Sam"), Json, Ct);
        var delete = await App.Client(Owner).DeleteAsync($"/api/investigate/workflows/{workflow.Id}", Ct);
        var after = await App.Client(Builder).GetAsync($"/api/investigate/workflows/{workflow.Id}", Ct);
        var oldRun = await App.Client(Builder).GetAsync($"/api/investigate/runs/{run.Id}", Ct);

        Assert.Equal(run.Id, Assert.Single(all!).Id);
        Assert.Empty(mine!);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldRun.StatusCode);
    }

    [Fact]
    public async Task RunHistory_Filters_ByWorkflowStateAndName()
    {
        var a = await App.CreateAsync(Builder, "Alpha check", OneStep);
        var b = await App.CreateAsync(Builder, "Beta check", OneStep);
        App.Agents.Answers[Analyst] = (_, _) => throw new Skanyxx.Module.Investigate.Agents.AgentCallException("no");
        var c = await App.CreateAsync(Builder, "Gamma", [Agent("s1", Analyst, "x")]);
        foreach (var w in new[] { a, b, c })
            await App.WaitForAsync((await App.StartAsync(Builder, w.Id, "x")).Id, RunState.Succeeded, RunState.Failed);
        var bea = App.Client(Builder);

        var byWorkflow = await bea.GetFromJsonAsync<List<RunSummaryDto>>($"/api/investigate/runs?workflowId={a.Id}", Json, Ct);
        var failed = await bea.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs?state=failed", Json, Ct);
        var named = await bea.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs?q=check", Json, Ct);
        var wildcard = await bea.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs?q=%25", Json, Ct);

        Assert.Equal("Alpha check", Assert.Single(byWorkflow!).WorkflowName);
        Assert.Equal("Gamma", Assert.Single(failed!).WorkflowName);
        Assert.Equal(["Beta check", "Alpha check"], named!.Select(r => r.WorkflowName));
        Assert.Empty(wildcard!);
    }

    [Fact]
    public async Task AnInvalidWorkflow_IsA400_WithEveryReason()
    {
        var response = await App.Client(Builder).PostAsJsonAsync("/api/investigate/workflows",
            new { name = "", steps = new[] { Agent("s1", Researcher, "{{steps.s2}}"), Agent("s1", "nope", "") } }, Json, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Give the workflow a name", text);
        Assert.Contains("Two steps share the id", text);
    }
}
