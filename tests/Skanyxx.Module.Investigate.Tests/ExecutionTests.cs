using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>The worker: order of steps, answers flowing into later prompts, failure, cancel and the memory card (timeout: <see cref="TimeoutTests"/>).</summary>
public sealed class ExecutionTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    [Fact]
    public async Task Sequential_RunsInOrder_AndEachPromptReadsTheEarlierAnswer()
    {
        var workflow = await App.CreateAsync(Builder, "Checkout",
            [Agent("s1", Researcher, "Find: {{input}}"), Agent("s2", Analyst, "Weigh: {{steps.s1}}"), Agent("s3", Writer, "Write: {{steps.s2}}")]);

        var run = await App.StartAsync(Builder, workflow.Id, "slow checkout");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal([Researcher, Analyst, Writer], App.Agents.Calls.Select(c => c.Agent));
        Assert.Equal(
        [
            "Find: " + Prompts.Input("slow checkout"),
            "Weigh: " + Prompts.Step("s1", $"{Researcher} says: Find: " + Prompts.Input("slow checkout")),
            "Write: " + Prompts.Step("s2", $"{Analyst} says: Weigh: " + Prompts.Step("s1", $"{Researcher} says: Find: " + Prompts.Input("slow checkout")))
        ], App.Agents.Calls.Select(c => Prompts.Plain(c.Prompt)));
        Assert.All(done.StepRuns, s => Assert.Equal(StepState.Succeeded, s.State));
        Assert.All(done.StepRuns, s => Assert.True(s.EndedAt >= s.StartedAt));
        var s1 = done.StepRuns.Single(s => s.StepId == "s1");
        var s2 = done.StepRuns.Single(s => s.StepId == "s2");
        Assert.True(s2.StartedAt >= s1.EndedAt, "s2 started before s1 ended");
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.EndedAt);
        Assert.Null(done.Failure);
    }

    [Fact]
    public async Task Parallel_StartsEveryStep_BeforeAnyEnds_AndTheNextStepWaitsForAll()
    {
        // Each parallel agent answers only once all three have been called: run one after another, this would time out.
        var started = 0;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Gate(string prompt, CancellationToken ct)
        {
            if (Interlocked.Increment(ref started) == 3)
                all.SetResult();
            await all.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return $"seen {prompt}";
        }
        App.Agents.Answers[Researcher] = Gate;
        App.Agents.Answers[Analyst] = Gate;
        App.Agents.Answers["kagent/researcher-2"] = Gate;
        App.Agents.Agents.Add(ReadOnly("kagent/researcher-2"));
        var workflow = await App.CreateAsync(Builder, "Fan out",
        [
            Group("g1", GroupMode.Parallel, Agent("p1", Researcher, "a"), Agent("p2", Analyst, "b"), Agent("p3", "kagent/researcher-2", "c")),
            Agent("s4", Writer, "{{steps.p1}} | {{steps.p2}} | {{steps.p3}}")
        ]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded, RunState.Failed);

        Assert.Equal(RunState.Succeeded, done.State);
        Assert.Equal(Writer, App.Agents.Calls.Last().Agent);
        Assert.Equal($"{Prompts.Step("p1", "seen a")} | {Prompts.Step("p2", "seen b")} | {Prompts.Step("p3", "seen c")}", Prompts.Plain(App.Agents.Calls.Last().Prompt));
        var group = done.StepRuns.Single(s => s.StepId == "g1");
        Assert.Equal(StepState.Succeeded, group.State);
        Assert.True(done.StepRuns.Single(s => s.StepId == "s4").StartedAt >= group.EndedAt);
    }

    [Fact]
    public async Task AFailedStep_FailsTheRun_WithAReadableReason_CancelsItsSiblings_AndSkipsTheRest()
    {
        App.Agents.Answers[Analyst] = (_, _) => throw new Skanyxx.Module.Investigate.Agents.AgentCallException("kagent answered HTTP 500");
        App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(20), ct); return "never"; };
        var workflow = await App.CreateAsync(Builder, "Breaks",
            [Group("g1", GroupMode.Parallel, Agent("slow", Researcher, "x", "Slow one"), Agent("bad", Analyst, "y", "Bad one")), Agent("after", Writer, "z")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal("Step 'Bad one' (kagent/analyst) failed: kagent answered HTTP 500.", done.Failure);
        var states = done.StepRuns.ToDictionary(s => s.StepId, s => s.State);
        Assert.Equal(StepState.Failed, states["bad"]);
        Assert.Equal(StepState.Cancelled, states["slow"]);
        Assert.Equal(StepState.Failed, states["g1"]);
        Assert.Equal(StepState.Skipped, states["after"]);
        Assert.Equal("kagent answered HTTP 500", done.StepRuns.Single(s => s.StepId == "bad").Error);
        Assert.DoesNotContain(App.Agents.Calls, c => c.Agent == Writer);
        Assert.Empty(App.Cards.Investigations);
        Assert.Null(done.CardKey);
    }

    [Fact]
    public async Task Cancel_StopsARunningRun_AndARunThatEndedIsA409()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Agents.Answers[Researcher] = async (_, ct) => { entered.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(30), ct); return "never"; };
        var workflow = await App.CreateAsync(Builder, "Long", [Agent("s1", Researcher, "x"), Agent("s2", Analyst, "y")]);
        var run = await App.StartAsync(Builder, workflow.Id, "x");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var cancel = await App.Client(Builder).PostAsync($"/api/investigate/runs/{run.Id}/cancel", null, Ct);
        var done = await App.WaitForAsync(run.Id, RunState.Cancelled);
        var again = await App.Client(Builder).PostAsync($"/api/investigate/runs/{run.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(StepState.Cancelled, done.StepRuns.Single(s => s.StepId == "s1").State);
        Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "s2").State);
        Assert.Equal("Cancelled.", done.Failure);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.DoesNotContain(App.Agents.Calls, c => c.Agent == Analyst);
    }

    [Fact]
    public async Task ASuccess_WritesOneMemoryCard_WithTheLastAnswer_AndTheRunLinksToIt()
    {
        var workflow = await App.CreateAsync(Builder, "Card", [Agent("s1", Researcher, "{{input}}"), Agent("s2", Writer, "final")]);

        var run = await App.StartAsync(Builder, workflow.Id, "why is billing late?");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        var card = Assert.Single(App.Cards.Investigations);
        Assert.Equal((run.Id, Builder, "Card", "why is billing late?", $"{Writer} says: final"), (card.RunId, card.OwnerUserId, card.WorkflowName, card.Input, card.Finding));
        Assert.Equal($"personal:{Builder}", done.CardScope);
        Assert.Equal($"investigation-{run.Id:N}", done.CardKey);
    }

    [Fact]
    public async Task Rerun_StartsTheSameInputAgain()
    {
        var workflow = await App.CreateAsync(Builder, "Again", [Agent("s1", Researcher, "{{input}}")]);
        var first = await App.StartAsync(Builder, workflow.Id, "same question");
        await App.WaitForAsync(first.Id, RunState.Succeeded);

        var rerun = await App.Client(Builder).PostAsync($"/api/investigate/runs/{first.Id}/rerun", null, Ct);
        var second = (await rerun.Content.ReadFromJsonAsync<RunDto>(InvestigateApp.Json, Ct))!;
        await App.WaitForAsync(second.Id, RunState.Succeeded);

        Assert.Equal(HttpStatusCode.Accepted, rerun.StatusCode);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([Prompts.Input("same question"), Prompts.Input("same question")], App.Agents.Calls.Select(c => Prompts.Plain(c.Prompt)));
    }

    [Fact]
    public async Task TooManyActiveRuns_IsA429()
    {
        App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return "x"; };
        var workflow = await App.CreateAsync(Builder, "Busy", [Agent("s1", Researcher, "x")]);
        for (var i = 0; i < 3; i++)
            await App.StartAsync(Builder, workflow.Id, "x");

        var fourth = await App.Client(Builder).PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.Id, input = "x" }, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
    }

    [Fact]
    public async Task AnAgentThatIsNoLongerMerged_IsRefusedAtStart_AndKagentDownIs502()
    {
        var workflow = await App.CreateAsync(Builder, "Gone", [Agent("s1", Writer, "x")]);
        App.Agents.Agents.RemoveAll(a => a.Ref == Writer);

        var refused = await App.Client(Builder).PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.Id, input = "x" }, Ct);
        App.Agents.Down = true;
        var down = await App.Client(Builder).PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow.Id, input = "x" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("team-a/writer", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
        Assert.Empty(App.Agents.Calls);
    }
}
