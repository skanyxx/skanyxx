using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>
/// D371: a supervisor reaches the work of the people in their own teams only, the way Analytics scopes them; the owner
/// reaches everyone's. Sam supervises billing (with Bea); Ben is in sales; Olga is the owner.
/// </summary>
public sealed class SupervisorScopeTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private static readonly WorkflowStep[] OneStep = [Agent("s1", Researcher, "{{input}}")];

    private async Task<(WorkflowDto Workflow, RunDto Run)> RunAsync(string who)
    {
        var workflow = await App.CreateAsync(who, $"{who}'s", OneStep);
        var run = await App.StartAsync(who, workflow.Id, $"{who}'s private question");
        await App.WaitForAsync(run.Id, RunState.Succeeded);
        return (workflow, run);
    }

    [Fact]
    public async Task ASupervisor_GetsNotFound_OnEveryEndpoint_ForSomeoneOutsideTheirTeams()
    {
        var (workflow, run) = await RunAsync(OtherBuilder);
        var sam = App.Client(Supervisor);
        var calls = App.Agents.Calls.Count;

        var workflows = await sam.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        var runs = await sam.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        HttpResponseMessage[] responses =
        [
            await sam.GetAsync($"/api/investigate/workflows/{workflow.Id}", Ct),
            await sam.PutAsJsonAsync($"/api/investigate/workflows/{workflow.Id}", new { name = "Taken", description = "", steps = OneStep }, Json, Ct),
            await sam.PostAsync($"/api/investigate/workflows/{workflow.Id}/duplicate", null, Ct),
            await sam.DeleteAsync($"/api/investigate/workflows/{workflow.Id}", Ct),
            await sam.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.Id, input = "x" }, Ct),
            await sam.GetAsync($"/api/investigate/runs/{run.Id}", Ct),
            await sam.PostAsync($"/api/investigate/runs/{run.Id}/cancel", null, Ct),
            await sam.PostAsync($"/api/investigate/runs/{run.Id}/rerun", null, Ct),
            await sam.GetAsync($"/api/investigate/runs/{run.Id}/report.json", Ct),
            await sam.GetAsync($"/api/investigate/runs/{run.Id}/report.pdf", Ct),
        ];

        Assert.Empty(workflows!);
        Assert.Empty(runs!);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        foreach (var r in responses)
            Assert.DoesNotContain("private question", await r.Content.ReadAsStringAsync(Ct));
        Assert.Equal(calls, App.Agents.Calls.Count);
        Assert.Equal($"{OtherBuilder}'s", (await App.Client(OtherBuilder).GetFromJsonAsync<WorkflowDto>($"/api/investigate/workflows/{workflow.Id}", Json, Ct))!.Name);
    }

    [Fact]
    public async Task ASupervisor_ReachesTheirTeam_AndTheOwnerReachesEveryone()
    {
        var bea = await RunAsync(Builder);
        var ben = await RunAsync(OtherBuilder);

        var samRuns = await App.Client(Supervisor).GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        var samWorkflows = await App.Client(Supervisor).GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        var samReads = await App.Client(Supervisor).GetFromJsonAsync<RunDto>($"/api/investigate/runs/{bea.Run.Id}", Json, Ct);
        var olgaRuns = await App.Client(Owner).GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        var olgaWorkflows = await App.Client(Owner).GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        var olgaReads = await App.Client(Owner).GetAsync($"/api/investigate/runs/{ben.Run.Id}/report.json", Ct);

        Assert.Equal([bea.Run.Id], samRuns!.Select(r => r.Id));
        Assert.Equal(bea.Workflow.Id, Assert.Single(samWorkflows!).Id);
        Assert.True(Assert.Single(samWorkflows!).CanEdit);
        Assert.Equal($"{Builder}'s private question", samReads!.Input);
        Assert.Equal([ben.Run.Id, bea.Run.Id], olgaRuns!.Select(r => r.Id));
        Assert.Equal(2, olgaWorkflows!.Count);
        Assert.Equal(HttpStatusCode.OK, olgaReads.StatusCode);
    }

    [Fact]
    public async Task LeavingTheTeam_EndsASupervisorsReach_AtOnce()
    {
        var bea = await RunAsync(Builder);
        App.Org.Teams[0] = new TeamDto("billing", "Billing", "finance", [Builder]);

        Assert.Equal(HttpStatusCode.NotFound, (await App.Client(Supervisor).GetAsync($"/api/investigate/runs/{bea.Run.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task AWorkflow_SharesWithATeamWhoseSlugHasDots_UpTo128Characters()
    {
        var slug = "eu.billing_ops@acme-" + new string('x', 108);
        App.Org.Teams.Add(new TeamDto(slug, "EU billing ops", "finance", [Builder, OtherBuilder]));

        var workflow = await App.CreateAsync(Builder, "Shared far", OneStep, slug);
        var ben = await App.Client(OtherBuilder).GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);

        Assert.Equal(128, slug.Length);
        Assert.Equal(slug, workflow.SharedTeam);
        Assert.Equal(workflow.Id, Assert.Single(ben!).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await App.Client(Builder).PostAsJsonAsync("/api/investigate/workflows",
            new { name = "Too long", description = "", sharedTeam = slug + "y", steps = OneStep }, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task ASupervisor_DoesNotReachTheOwnerOrAnotherSupervisor_InTheirOwnTeam()
    {
        App.Org.Teams[0] = new TeamDto("billing", "Billing", "finance", [Builder, Supervisor, Owner, OtherSupervisor]);
        var olga = await RunAsync(Owner);
        var sue = await RunAsync(OtherSupervisor);
        var shared = await App.CreateAsync(OtherSupervisor, "Sue's shared", OneStep, "billing");
        var sam = App.Client(Supervisor);

        var runs = await sam.GetFromJsonAsync<List<RunSummaryDto>>("/api/investigate/runs", Json, Ct);
        var workflows = await sam.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);
        HttpResponseMessage[] hidden =
        [
            await sam.GetAsync($"/api/investigate/runs/{olga.Run.Id}", Ct),
            await sam.GetAsync($"/api/investigate/runs/{sue.Run.Id}", Ct),
            await sam.PutAsJsonAsync($"/api/investigate/workflows/{olga.Workflow.Id}", new { name = "Taken", description = "", steps = OneStep }, Json, Ct),
            await sam.PutAsJsonAsync($"/api/investigate/workflows/{sue.Workflow.Id}", new { name = "Taken", description = "", steps = OneStep }, Json, Ct),
        ];
        var editShared = await sam.PutAsJsonAsync($"/api/investigate/workflows/{shared.Id}",
            new { name = "Taken", description = "", sharedTeam = "billing", allowChainedWrites = true, steps = OneStep }, Json, Ct);

        Assert.Empty(runs!);
        Assert.Equal(shared.Id, Assert.Single(workflows!).Id);
        Assert.False(Assert.Single(workflows!).CanEdit);
        Assert.All(hidden, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(HttpStatusCode.Forbidden, editShared.StatusCode);
        Assert.Equal("Sue's shared", (await App.Client(OtherSupervisor).GetFromJsonAsync<WorkflowDto>($"/api/investigate/workflows/{shared.Id}", Json, Ct))!.Name);
    }

    [Fact]
    public async Task WhenTeamsCannotBeListed_ASupervisorKeepsTheirTeamShares_AndReachesNoOneElsesWork()
    {
        var bea = await RunAsync(Builder);
        var shared = await App.CreateAsync(Builder, "Bea's shared", OneStep, "billing");
        App.Org.TeamsListable = false;
        var sam = App.Client(Supervisor);

        var workflows = await sam.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/investigate/workflows", Json, Ct);

        Assert.Equal(shared.Id, Assert.Single(workflows!).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await sam.GetAsync($"/api/investigate/runs/{bea.Run.Id}", Ct)).StatusCode);
    }
}
