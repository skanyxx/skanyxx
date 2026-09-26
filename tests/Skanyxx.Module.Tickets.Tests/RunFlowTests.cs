using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class RunFlowTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    [Fact]
    public async Task DefaultPipeline_RunsEveryStageThroughKAgent_AndStopsAtTheHumanGate()
    {
        var run = await App.StartRunAsync();

        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal(["plan", "review-plan", "code", "qa-code", "review"], waiting.StageRuns.Select(s => s.StageId));
        Assert.Equal(
            ["ticket-planner", "ticket-plan-reviewer", "ticket-coder", "ticket-qa", "ticket-reviewer"],
            KAgent.Calls.Select(c => c.Agent));
        Assert.All(KAgent.Calls, c =>
        {
            Assert.Equal("kagent", c.Namespace);
            Assert.Equal("message/send", c.Method);
            Assert.Equal("0.3", c.A2AVersion);
            Assert.Equal("skanyxx-tickets", c.UserId);
            Assert.True(c.Blocking);
        });
        var review = waiting.StageRuns[^1];
        Assert.Equal(StageState.AwaitingHuman, review.State);
        Assert.Equal(Verdict.Pass, review.Verdict);
        Assert.Null(review.EndedAt);
    }

    [Fact]
    public async Task Approve_FinishesTheRun()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.DecideAsync(run.Id, "approve", "looks right");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = done.StageRuns[^1];
        Assert.Equal(StageState.Passed, review.State);
        Assert.Equal(TicketsApp.User, review.DecidedBy);
        Assert.Contains(review.Warnings, w => w.Contains("approved: looks right"));
    }

    [Fact]
    public async Task EmptyDecision_IsRejected_NotAnApproval()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.Client().PostAsJsonAsync($"/api/tickets/runs/{run.Id}/decision", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RunState.AwaitingHuman, (await App.GetRunAsync(run.Id)).State);
    }

    [Fact]
    public async Task QaFail_LoopsBackToCode_WithTheFullRejection_AndKeepsEveryAttempt()
    {
        KAgent.Script("ticket-qa", new AgentReply.Completed(FakeKAgent.Fail), new AgentReply.Completed(FakeKAgent.Pass));
        KAgent.Script("ticket-coder", new AgentReply.Completed("first change"), new AgentReply.Completed("second change"));

        var run = await App.StartRunAsync();
        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal(["plan", "review-plan", "code", "qa-code", "code", "qa-code", "review"], waiting.StageRuns.Select(s => s.StageId));
        var firstQa = waiting.StageRuns[3];
        Assert.Equal((1, StageState.Failed, Verdict.Fail), (firstQa.Attempt, firstQa.State, firstQa.Verdict));
        Assert.Contains(firstQa.Warnings, w => w.Contains("looping back to 'Code' (pass 2)"));
        Assert.Equal(["first change", "second change"], waiting.StageRuns.Where(s => s.StageId == "code").Select(s => s.Output));
        Assert.Equal([1, 2], waiting.StageRuns.Where(s => s.StageId == "code").Select(s => s.Attempt));
        Assert.Equal(1, waiting.Loops["qa-code"]);

        var rerun = KAgent.CallsTo("ticket-coder")[1].Prompt;
        Assert.Contains("## Do this again — pass 2", rerun);
        Assert.Contains("rejected by 'QA the change'", rerun);
        Assert.Contains("blocker: no test for the double click", rerun);
        Assert.Contains(FakeKAgent.Fail, rerun);
        Assert.DoesNotContain("Do this again", KAgent.CallsTo("ticket-coder")[0].Prompt);
        // The rejected attempt is not handed back as "prior work": only QA's report of it is.
        Assert.DoesNotContain("first change", rerun);
    }

    [Fact]
    public async Task ExhaustedLoopBudget_FailsTheRun_AndSaysWhy()
    {
        KAgent.Script("ticket-qa", Enumerable.Repeat<AgentReply>(new AgentReply.Completed(FakeKAgent.Fail), 3).ToArray());

        var run = await App.StartRunAsync();
        var failed = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal(3, failed.StageRuns.Count(s => s.StageId == "qa-code"));
        Assert.Equal(3, KAgent.CallsTo("ticket-coder").Count);
        Assert.Empty(KAgent.CallsTo("ticket-reviewer"));
        Assert.Contains(failed.StageRuns[^1].Warnings,
            w => w == "loop budget spent: 'qa-code' already sent this run back 2 time(s), max_loops is 2");
    }

    [Fact]
    public async Task VerdictIsNotState_FailWithoutOptIn_IsRecordedAndTheRunMovesOn()
    {
        await App.SavePipelineAsync("record-only", Pipeline(
            Stage("qa", "qa", "ticket-qa"),
            Stage("review", "review", "ticket-reviewer")));
        KAgent.Script("ticket-qa", new AgentReply.Completed(FakeKAgent.Fail));

        var run = await App.StartRunAsync(pipelineId: "record-only");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal((StageState.Passed, Verdict.Fail), (done.StageRuns[0].State, done.StageRuns[0].Verdict));
    }

    [Fact]
    public async Task FailOnVerdict_WithNoStatedVerdict_FailsClosed()
    {
        await App.SavePipelineAsync("strict", Pipeline(Stage("qa", "qa", "ticket-qa", ("failOnVerdict", true))));
        KAgent.Script("ticket-qa", new AgentReply.Completed("I think it is mostly fine."));

        var run = await App.StartRunAsync(pipelineId: "strict");
        var failed = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal((StageState.Failed, Verdict.Unknown), (failed.StageRuns[0].State, failed.StageRuns[0].Verdict));
        Assert.Contains("stated no verdict; treated as a failure because this stage fails on its verdict", failed.StageRuns[0].Warnings);
    }

    [Fact]
    public async Task HumanReject_FollowsOnFail_AndTheNoteReachesTheReRunStage()
    {
        await App.SavePipelineAsync("gated-loop", Pipeline(
            Stage("code", "code", "ticket-coder"),
            Stage("review", "review", "ticket-reviewer",
                ("gate", "human"), ("onFail", "goto"), ("goto", "code"), ("maxLoops", 1))));

        var run = await App.StartRunAsync(pipelineId: "gated-loop");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        await App.DecideAsync(run.Id, "reject", "Use the existing idempotency table.");
        var again = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var gate = again.StageRuns[1];
        Assert.Equal((StageState.Failed, Verdict.Pass), (gate.State, gate.Verdict));
        var rerun = KAgent.CallsTo("ticket-coder")[1].Prompt;
        Assert.Contains("## Do this again — pass 2", rerun);
        Assert.Contains("Use the existing idempotency table.", rerun);

        await App.DecideAsync(run.Id, "reject", "still no");
        var failed = await App.WaitForAsync(run.Id, RunState.Failed);
        Assert.Contains(failed.StageRuns[^1].Warnings, w => w.StartsWith("loop budget spent"));
    }

    [Fact]
    public async Task HumanReject_WithStop_FailsTheRun()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        await App.DecideAsync(run.Id, "reject");
        var failed = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal(StageState.Failed, failed.StageRuns[^1].State);
    }

    [Fact]
    public async Task DecidingARunThatIsNotWaiting_IsAConflict()
    {
        await App.SavePipelineAsync("no-gate", Pipeline(Stage("plan", "plan", "ticket-planner")));
        var run = await App.StartRunAsync(pipelineId: "no-gate");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        var response = await App.DecideAsync(run.Id, "approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("http")]
    public async Task UnreachableQaAgent_WritesAFallback_FailsClosed_AndDoesNotPayForALoop(string how)
    {
        KAgent.Script("ticket-qa", how == "failed"
            ? new AgentReply.Failed("LLM error: 429 rate limited")
            : new AgentReply.Http(404));

        var run = await App.StartRunAsync();
        var waiting = await App.WaitForAsync(run.Id, RunState.Failed);

        var qa = waiting.StageRuns.Single(s => s.StageId == "qa-code");
        Assert.True(qa.Degraded);
        Assert.Equal((StageState.Failed, Verdict.Unknown), (qa.State, qa.Verdict));
        Assert.StartsWith("> **Deterministic fallback.**", qa.Output);
        // A category only: kagent's own error text (hostnames, provider errors) stays in the log.
        Assert.Contains(qa.Warnings, w => w.StartsWith("agent 'ticket-qa' (kagent/ticket-qa): ") &&
                                          (how == "failed" ? w.Contains("the agent's task ended 'failed'") : w.Contains("HTTP 404")));
        Assert.DoesNotContain(qa.Warnings, w => w.Contains("LLM error"));
        Assert.Contains("an agent in this stage did not answer; treated as a failure because this stage fails on its verdict", qa.Warnings);
        Assert.Contains("no agent answered this stage, so the run stops instead of looping back", qa.Warnings);
        Assert.Single(KAgent.CallsTo("ticket-coder"));
    }

    [Fact]
    public async Task ContextModes_ControlWhichPriorStagesTheAgentSees()
    {
        await App.SavePipelineAsync("context", Pipeline(
            Stage("plan", "plan", "ticket-planner"),
            Stage("code", "code", "ticket-coder"),
            Stage("last", "qa", "ticket-qa", ("context", "last")),
            Stage("none", "review", "ticket-reviewer", ("context", "none"))));
        KAgent.Script("ticket-planner", new AgentReply.Completed("PLAN-OUTPUT"));
        KAgent.Script("ticket-coder", new AgentReply.Completed("CODE-OUTPUT"));

        var run = await App.StartRunAsync(pipelineId: "context");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        var code = KAgent.CallsTo("ticket-coder")[0].Prompt;
        var last = KAgent.CallsTo("ticket-qa")[0].Prompt;
        var none = KAgent.CallsTo("ticket-reviewer")[0].Prompt;
        Assert.Contains("PLAN-OUTPUT", code);
        Assert.Contains("CODE-OUTPUT", last);
        Assert.DoesNotContain("PLAN-OUTPUT", last);
        Assert.DoesNotContain("PLAN-OUTPUT", none);
        Assert.Contains("_This is the first stage of the run._", none);
    }

    [Fact]
    public async Task MultiAgentStage_HandsEachAnswerOn_AndTheLastAnswerIsTheStages()
    {
        await App.SavePipelineAsync("chain", Pipeline(new Dictionary<string, object?>
        {
            ["id"] = "code", ["kind"] = "code", ["skills"] = Array.Empty<string>(),
            ["agents"] = new[] { new { id = "draft", agent = "kagent/ticket-coder" }, new { id = "refine", agent = "kagent/ticket-refiner" } }
        }));
        KAgent.Script("ticket-coder", new AgentReply.Completed("DRAFT"));
        KAgent.Script("ticket-refiner", new AgentReply.Completed("REFINED"));

        var run = await App.StartRunAsync(pipelineId: "chain");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal("REFINED", done.StageRuns[0].Output);
        Assert.Equal(["draft", "refine"], done.StageRuns[0].Turns.Select(t => t.AgentId));
        Assert.Matches(@"## Handed to you by draft\n\nContinue from this\. Do not restate it\.\n\n<data-[0-9a-f]{12}>\nDRAFT\n</data-[0-9a-f]{12}>",
            KAgent.CallsTo("ticket-refiner")[0].Prompt);
        Assert.DoesNotContain("DRAFT", KAgent.CallsTo("ticket-coder")[0].Prompt);
    }

    [Fact]
    public async Task Dataset_HasOneRowPerStageAttemptAndAgent_WithTheExactPrompt()
    {
        KAgent.Script("ticket-qa", new AgentReply.Completed(FakeKAgent.Fail), new AgentReply.Completed(FakeKAgent.Pass));
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        var response = await App.Client().GetAsync($"/api/tickets/runs/{run.Id}/dataset");
        var rows = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonSerializer.Deserialize<DatasetRow>(l, TicketsApp.Json)!).ToList();

        Assert.Equal("application/x-ndjson; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(7, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => (r.StageId, r.Attempt, r.AgentId)).Distinct().Count());
        Assert.Equal(KAgent.Calls.Select(c => c.Prompt), rows.Select(r => r.Prompt));
        var qa = rows.Where(r => r.StageId == "qa-code").ToList();
        Assert.Equal([(1, StageState.Failed, Verdict.Fail), (2, StageState.Passed, Verdict.Pass)],
            qa.Select(r => (r.Attempt, r.State, r.Verdict)));
        Assert.All(rows, r => Assert.Equal("SDB-1", r.Ticket));
    }

    [Fact]
    public async Task Cancel_StopsTheRun_AndAnInFlightStageIsDropped()
    {
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", new AgentReply.Blocked(release, "late plan"));
        var run = await App.StartRunAsync();
        await WaitUntil(() => KAgent.CallsTo("ticket-planner").Count == 1);

        var cancel = await App.Client().PostAsync($"/api/tickets/runs/{run.Id}/cancel", null);
        await App.SavePipelineAsync("after", Pipeline(Stage("plan", "plan", "ticket-planner")));
        var after = await App.StartRunAsync(pipelineId: "after");
        release.SetResult();
        // One worker: once the later run has finished, the cancelled run's in-flight step has ended too.
        await App.WaitForAsync(after.Id, RunState.Succeeded);
        var cancelled = await App.GetRunAsync(run.Id);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(RunState.Cancelled, cancelled.State);
        Assert.Empty(App.Errors);
        Assert.Empty(cancelled.StageRuns);
        Assert.Empty(KAgent.CallsTo("ticket-plan-reviewer"));
    }

    [Fact]
    public async Task CancelDuringAMultiAgentStage_CallsNoFurtherAgent()
    {
        await App.SavePipelineAsync("three", Pipeline(new Dictionary<string, object?>
        {
            ["id"] = "code", ["kind"] = "code", ["skills"] = Array.Empty<string>(),
            ["agents"] = new[]
            {
                new { id = "draft", agent = "kagent/ticket-coder" },
                new { id = "refine", agent = "kagent/ticket-refiner" },
                new { id = "polish", agent = "kagent/ticket-planner" }
            }
        }));
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-coder", new AgentReply.Blocked(release, "DRAFT"));
        var run = await App.StartRunAsync(pipelineId: "three");
        await WaitUntil(() => KAgent.CallsTo("ticket-coder").Count == 1);

        var cancel = await App.Client().PostAsync($"/api/tickets/runs/{run.Id}/cancel", null);
        await App.SavePipelineAsync("after", Pipeline(Stage("plan", "plan", "ticket-plan-reviewer")));
        var after = await App.StartRunAsync(pipelineId: "after");
        release.SetResult();
        // One worker: once the later run has finished, the cancelled run's in-flight step has ended too.
        await App.WaitForAsync(after.Id, RunState.Succeeded);
        var cancelled = await App.GetRunAsync(run.Id);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(RunState.Cancelled, cancelled.State);
        Assert.Empty(cancelled.StageRuns);
        Assert.Empty(KAgent.CallsTo("ticket-refiner"));
        Assert.Empty(KAgent.CallsTo("ticket-planner"));
        Assert.Empty(App.Errors);
    }

    [Theory]
    [InlineData("## Verdict\nPASS\n\n## Findings\n1. minor: naming.")]
    [InlineData("```markdown\n## Verdict\nFAIL\n\n## Findings\n1. blocker: no test.\n\nThe ticket's tracker template:\n```text\nVerdict: PASS\n```\n```")]
    public async Task AQaAnswerNotOpeningWithTheVerdictLine_FailsClosed_AndLoopsBackToCode(string answer)
    {
        KAgent.Script("ticket-qa", new AgentReply.Completed(answer));

        var run = await App.StartRunAsync();
        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal(["plan", "review-plan", "code", "qa-code", "code", "qa-code", "review"], waiting.StageRuns.Select(s => s.StageId));
        var firstQa = waiting.StageRuns[3];
        Assert.Equal((StageState.Failed, Verdict.Unknown), (firstQa.State, firstQa.Verdict));
        Assert.Contains("stated no verdict; treated as a failure because this stage fails on its verdict", firstQa.Warnings);
        Assert.Equal(2, KAgent.CallsTo("ticket-coder").Count);
    }

    [Fact]
    public async Task EditingAPipeline_DoesNotChangeARunThatAlreadyStarted()
    {
        await App.SavePipelineAsync("editable", Pipeline(Stage("plan", "plan", "ticket-planner", ("gate", "human"))));
        var run = await App.StartRunAsync(pipelineId: "editable");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        await App.SavePipelineAsync("editable", Pipeline(
            Stage("plan", "plan", "ticket-planner", ("gate", "human")), Stage("code", "code", "ticket-coder")));
        await App.DecideAsync(run.Id, "approve");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Single(done.Stages);
        Assert.Empty(KAgent.CallsTo("ticket-coder"));
    }

    [Fact]
    public async Task StartRun_SnapshotsTheTicket()
    {
        var run = await App.StartRunAsync("SDB-1");

        Assert.Equal("Refund button double-charges", run.Ticket.Title);
        Assert.Equal(RunState.Pending, run.State);
        Assert.Equal(TicketsApp.User, run.CreatedBy);
    }

    [Theory]
    [InlineData("SDB-404", "ticket-fix", HttpStatusCode.NotFound)]
    [InlineData("SDB-1", "no-such-pipeline", HttpStatusCode.NotFound)]
    [InlineData("not a key", "ticket-fix", HttpStatusCode.BadRequest)]
    public async Task StartRun_RejectsUnknownTicketsAndPipelines(string ticketKey, string pipelineId, HttpStatusCode expected)
    {
        var response = await App.Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey, pipelineId });

        Assert.Equal(expected, response.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Empty(db.Runs);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(50);
        }
    }
}
