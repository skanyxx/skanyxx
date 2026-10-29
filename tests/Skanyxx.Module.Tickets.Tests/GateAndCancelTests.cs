using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class GateAndCancelTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    /// <summary>D392: someone else's run is the 404 of no run; a supervisor cancels any.</summary>
    [Fact]
    public async Task OnlyTheCreatorOrASupervisor_Cancels_AnyoneElseSeesNoRun()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var byOther = await App.Client("bob").PostAsync($"/api/tickets/runs/{run.Id}/cancel", null, TestContext.Current.CancellationToken);
        var bySupervisor = await App.Client(TicketsApp.Supervisor).PostAsync($"/api/tickets/runs/{run.Id}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySupervisor.StatusCode);
    }

    /// <summary>
    /// D392: deciding resumes agent calls, so it is a supervisor's — any supervisor's. The starter without the role (a
    /// demoted supervisor) cannot, nor can anyone else below it.
    /// </summary>
    [Theory]
    [InlineData(TicketsApp.User, SkanyxxRoles.Employee, HttpStatusCode.Forbidden)]
    [InlineData("bob", SkanyxxRoles.Builder, HttpStatusCode.Forbidden)]
    [InlineData("bob", SkanyxxRoles.Supervisor, HttpStatusCode.OK)]
    [InlineData("olga", SkanyxxRoles.Owner, HttpStatusCode.OK)]
    [InlineData(TicketsApp.User, SkanyxxRoles.Supervisor, HttpStatusCode.OK)]
    public async Task OnlyASupervisorOrTheOwner_DecidesAGate(string userId, string role, HttpStatusCode expected)
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.ClientAs(userId, role).PostAsJsonAsync($"/api/tickets/runs/{run.Id}/decision", new { decision = "approve" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
            Assert.Equal(RunState.AwaitingHuman, (await App.GetRunAsync(run.Id)).State);
    }

    /// <summary>D392: losing supervisor (or being disabled) cancels the person's unfinished runs, a waiting gate included.</summary>
    [Fact]
    public async Task LosingSupervisor_CancelsThePersonsUnfinishedRuns_OnlyTheirs()
    {
        var mine = await App.StartRunAsync();
        await App.WaitForAsync(mine.Id, RunState.AwaitingHuman);
        var response = await App.Starter("carol").PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" },
            cancellationToken: TestContext.Current.CancellationToken);
        var theirs = (await response.Content.ReadFromJsonAsync<RunDto>(TicketsApp.Json, TestContext.Current.CancellationToken))!;
        await App.WaitForAsync(theirs.Id, RunState.AwaitingHuman);

        await using (var scope = App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new PrivilegesRevoked(TicketsApp.User, "roles saved without supervisor"),
                TestContext.Current.CancellationToken);
        var cancelled = await App.GetRunAsync(mine.Id);

        Assert.Equal(RunState.Cancelled, cancelled.State);
        Assert.Equal(StageState.Skipped, cancelled.StageRuns[^1].State);
        Assert.Contains("lost the right to run it (roles saved without supervisor)", cancelled.Error);
        Assert.Equal(RunState.AwaitingHuman, (await App.GetRunAsync(theirs.Id)).State);
        Assert.Equal(HttpStatusCode.Conflict, (await App.DecideAsync(mine.Id, "approve", userId: TicketsApp.Supervisor)).StatusCode);
    }

    [Fact]
    public async Task CancelAtAGate_ClosesTheWaitingStage()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        await App.Client().PostAsync($"/api/tickets/runs/{run.Id}/cancel", null, TestContext.Current.CancellationToken);
        var cancelled = await App.GetRunAsync(run.Id);

        var gate = cancelled.StageRuns[^1];
        Assert.Equal(RunState.Cancelled, cancelled.State);
        Assert.Equal(StageState.Skipped, gate.State);
        Assert.NotNull(gate.EndedAt);
        Assert.Contains($"{TicketsApp.User} cancelled the run at this gate", gate.Warnings);
    }

    [Fact]
    public async Task TwoDecisionsAtOnce_OneWins()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var answers = await Task.WhenAll(App.DecideAsync(run.Id, "approve"), App.DecideAsync(run.Id, "reject", userId: TicketsApp.Supervisor));
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded, RunState.Failed);

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.OK);
        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(done.StageRuns, s => s.StageId == "review");
    }

    [Fact]
    public async Task NulInANote_IsA400_NotA500()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.DecideAsync(run.Id, "reject", "bad\u0000note");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ARejectionIsShownOnce_NotToALaterPassOfTheSameStage()
    {
        await App.SavePipelineAsync("two-loops", Pipeline(
            Stage("plan", "plan", "ticket-planner"),
            Stage("code", "code", "ticket-coder"),
            Stage("qa", "qa", "ticket-qa", ("failOnVerdict", true), ("onFail", "goto"), ("goto", "code"), ("maxLoops", 1)),
            Stage("review", "review", "ticket-reviewer", ("gate", "human"), ("onFail", "goto"), ("goto", "plan"), ("maxLoops", 1))));
        KAgent.Script("ticket-qa", new AgentReply.Completed(FakeKAgent.Fail));

        var run = await App.StartRunAsync(pipelineId: "two-loops");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        await App.DecideAsync(run.Id, "reject", "rethink the plan");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var coder = KAgent.CallsTo("ticket-coder");
        Assert.Equal(3, coder.Count);
        Assert.Contains("## Do this again — pass 2", coder[1].Prompt);
        Assert.DoesNotContain("Do this again", coder[2].Prompt);
        Assert.Contains("rethink the plan", KAgent.CallsTo("ticket-planner")[1].Prompt);
    }

    [Fact]
    public async Task APersonRejectingAGateWhoseAgentWasDown_StillSendsTheWorkBack_WithTheirNote()
    {
        await App.SavePipelineAsync("gated-loop", Pipeline(
            Stage("code", "code", "ticket-coder"),
            Stage("review", "review", "ticket-reviewer", ("gate", "human"), ("onFail", "goto"), ("goto", "code"), ("maxLoops", 1))));
        KAgent.Script("ticket-reviewer", new AgentReply.Http(503));

        var run = await App.StartRunAsync(pipelineId: "gated-loop");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        await App.DecideAsync(run.Id, "reject", "I read it myself: the refund path has no test.");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal(2, KAgent.CallsTo("ticket-coder").Count);
        Assert.Contains("I read it myself: the refund path has no test.", KAgent.CallsTo("ticket-coder")[1].Prompt);
    }

    [Fact]
    public async Task RunsParkedAtAGate_CountTowardsTheCap()
    {
        await App.SavePipelineAsync("parked", Pipeline(Stage("plan", "plan", "ticket-planner", ("gate", "human"))));
        for (var i = 0; i < 5; i++)
            await App.WaitForAsync((await App.StartRunAsync(pipelineId: "parked")).Id, RunState.AwaitingHuman);

        var sixth = await App.Starter().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "parked" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    [Fact]
    public async Task Report_IsMarkdown_WithEveryAttempt()
    {
        KAgent.Script("ticket-qa", new AgentReply.Completed(FakeKAgent.Fail));
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.Client().GetAsync($"/api/tickets/runs/{run.Id}/report", TestContext.Current.CancellationToken);
        var md = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("text/markdown; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.StartsWith("# SDB-1: Refund button double-charges", md);
        Assert.Contains("## 04. QA the change — attempt 1\n\nstate: **failed** · verdict: **fail**", md);
        Assert.Contains("> ⚠ looping back to 'Code' (pass 2)", md);
        Assert.Contains("## 05. Code — attempt 2", md);
        Assert.Contains("## 07. Review — attempt 1\n\nstate: **awaiting_human**", md);
    }

    [Fact]
    public async Task TheCodeStage_SeesThePlanInFull()
    {
        var plan = "## Approach\n" + new string('p', 8_000);
        KAgent.Script("ticket-planner", new AgentReply.Completed(plan));

        var run = await App.StartRunAsync();
        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Contains(plan, KAgent.CallsTo("ticket-coder")[0].Prompt);
        Assert.DoesNotContain(waiting.StageRuns.Single(s => s.StageId == "code").Warnings, w => w.Contains("truncated"));
    }
}
