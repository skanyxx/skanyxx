using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>One bad run must end with a reason, never stall the worker or bill the model on every poll.</summary>
public sealed class RobustnessTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    [Fact]
    public async Task AnInternalError_FailsThatRun_AndTheNextRunStillFinishes()
    {
        ExplodingOn.Calls = 0;
        ExplodingOn.FailFirst = int.MaxValue;
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            services => services.AddScoped<IStageAgentClient, ExplodingOn>());
        await App.SavePipelineAsync("boom", Pipeline(Stage("plan", "plan", "ticket-boom")));
        await App.SavePipelineAsync("fine", Pipeline(Stage("plan", "plan", "ticket-planner")));

        var boom = await App.StartRunAsync(pipelineId: "boom");
        var fine = await App.StartRunAsync(pipelineId: "fine");
        var failed = await App.WaitForAsync(boom.Id, RunState.Failed);
        await App.WaitForAsync(fine.Id, RunState.Succeeded);
        await Task.Delay(1500);

        Assert.Equal("the run stopped on an internal error (InvalidOperationException) after 3 tries", failed.Error);
        Assert.Equal(3, ExplodingOn.Calls);
    }

    [Fact]
    public async Task OneFailedStep_IsRetried_NotFatal()
    {
        ExplodingOn.Calls = 0;
        ExplodingOn.FailFirst = 1;
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            services => services.AddScoped<IStageAgentClient, ExplodingOn>());
        await App.SavePipelineAsync("blip", Pipeline(Stage("plan", "plan", "ticket-boom")));

        var run = await App.StartRunAsync(pipelineId: "blip");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Null(done.Error);
        Assert.Equal(2, ExplodingOn.Calls);
    }

    [Fact]
    public async Task ParallelStarts_CannotExceedTheCap()
    {
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", Enumerable.Repeat<AgentReply>(new AgentReply.Blocked(release, "plan"), 30).ToArray());

        var answers = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
            App.Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" })));
        release.SetResult();

        Assert.Equal(5, answers.Count(a => a.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(25, answers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task AnswersWithNulOrAnOddShape_AreStored_OrFallBack_AndTheRunMovesOn()
    {
        await App.SavePipelineAsync("odd", Pipeline(Stage("plan", "plan", "ticket-planner"), Stage("code", "code", "ticket-coder")));
        KAgent.Script("ticket-planner", new AgentReply.Completed("plan\u0000text"));
        KAgent.Script("ticket-coder", new AgentReply.Raw("""{"jsonrpc":"2.0","id":"1","result":{"kind":"task","status":{"state":"completed"},"artifacts":[null]}}"""));

        var run = await App.StartRunAsync(pipelineId: "odd");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal("plantext", done.StageRuns[0].Output);
        Assert.True(done.StageRuns[1].Degraded);
        Assert.Contains(done.StageRuns[1].Warnings, w => w.Contains("kagent answered in an unexpected shape"));
    }

    [Fact]
    public async Task AnAgentRemovedFromTheAllowList_IsNotCalled_ByAPipelineSavedBefore()
    {
        await App.SavePipelineAsync("refined", Pipeline(Stage("plan", "plan", "ticket-refiner")));
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            configure: settings => settings.Remove("Tickets:AllowedAgents:5"));

        var run = await App.StartRunAsync(pipelineId: "refined");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Empty(KAgent.CallsTo("ticket-refiner"));
        Assert.True(done.StageRuns[0].Degraded);
        Assert.Contains(done.StageRuns[0].Warnings, w => w.Contains("the agent is not in Tickets:AllowedAgents"));
    }

    [Fact]
    public async Task ACutThroughAnEmoji_StillSaves()
    {
        await App.SavePipelineAsync("emoji", Pipeline(Stage("plan", "plan", "ticket-planner")));
        KAgent.Script("ticket-planner", new AgentReply.Completed(new string('x', 99_999) + "😀😀"));

        var run = await App.StartRunAsync(pipelineId: "emoji");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.StartsWith(new string('x', 99_999) + "\n\n_(truncated", done.StageRuns[0].Output);
    }

    [Fact]
    public async Task TheHandoff_CannotPushAMessagePastThePromptCeiling()
    {
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url, configure: s => s["Tickets:MaxPromptChars"] = "8000");
        await App.SavePipelineAsync("chain", Pipeline(new Dictionary<string, object?>
        {
            ["id"] = "code", ["kind"] = "code", ["skills"] = Array.Empty<string>(),
            ["agents"] = new[] { new { id = "draft", agent = "kagent/ticket-coder" }, new { id = "refine", agent = "kagent/ticket-refiner" } }
        }));
        KAgent.Script("ticket-coder", new AgentReply.Completed(new string('d', 10_000)));

        var run = await App.StartRunAsync(pipelineId: "chain");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.True(KAgent.CallsTo("ticket-refiner")[0].Prompt.Length <= 8_000 + 100);
        Assert.Contains(done.StageRuns[0].Warnings, w => w.StartsWith("the message to 'refine' (prompt plus handoff) was"));
    }

    [Fact]
    public async Task AChainWhoseFirstAgentNeverAnswered_DoesNotPassItsCheck()
    {
        await App.SavePipelineAsync("qa-chain", Pipeline(new Dictionary<string, object?>
        {
            ["id"] = "qa", ["kind"] = "qa", ["skills"] = Array.Empty<string>(), ["failOnVerdict"] = true,
            ["agents"] = new[] { new { id = "qa", agent = "kagent/ticket-qa" }, new { id = "refine", agent = "kagent/ticket-refiner" } }
        }));
        KAgent.Script("ticket-qa", new AgentReply.Http(500));
        KAgent.Script("ticket-refiner", new AgentReply.Completed(FakeKAgent.Pass));

        var run = await App.StartRunAsync(pipelineId: "qa-chain");
        var failed = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal((StageState.Failed, Verdict.Pass), (failed.StageRuns[0].State, failed.StageRuns[0].Verdict));
        Assert.Contains("an agent in this stage did not answer; treated as a failure because this stage fails on its verdict",
            failed.StageRuns[0].Warnings);
    }

    [Fact]
    public async Task HugeAnswers_AreCut_AndSaySo()
    {
        await App.SavePipelineAsync("big", Pipeline(Stage("plan", "plan", "ticket-planner")));
        KAgent.Script("ticket-planner", new AgentReply.Completed(new string('x', 150_000)));

        var run = await App.StartRunAsync(pipelineId: "big");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.EndsWith("_(truncated: showing 100,000 of 150,000 characters)_", done.StageRuns[0].Output);
        Assert.Contains("agent 'ticket-planner' answered 150,000 characters; cut to 100,000", done.StageRuns[0].Warnings);
    }

    [Fact]
    public async Task OneUser_CannotQueueRunsWithoutBound()
    {
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", new AgentReply.Blocked(release, "plan"));

        for (var i = 0; i < 5; i++)
            await App.StartRunAsync();
        var sixth = await App.Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" });
        var otherUser = await App.Client("bob").PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" });
        release.SetResult();

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, otherUser.StatusCode);
    }

    [Fact]
    public async Task ARunInFlight_ReportsWhichStageIsRunning()
    {
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", new AgentReply.Blocked(release, "plan"));
        var run = await App.StartRunAsync();

        var running = await App.WaitForAsync(run.Id, RunState.Running);
        release.SetResult();
        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal("plan", running.RunningStageId);
        Assert.Null(waiting.RunningStageId);
    }

    /// <summary>Throws a non-agent error for one agent, as a bug in stage code would.</summary>
    private sealed class ExplodingOn(IHttpClientFactory factory, Microsoft.Extensions.Options.IOptions<TicketsOptions> options) : IStageAgentClient
    {
        public static int Calls;

        /// <summary>How many calls to ticket-boom throw before it answers; int.MaxValue = always.</summary>
        public static int FailFirst = int.MaxValue;

        public Task<string> AskAsync(StageAgent agent, string prompt, CancellationToken ct)
        {
            if (agent.Name != "ticket-boom" || Interlocked.Increment(ref Calls) > FailFirst)
                return Real().AskAsync(agent, prompt, ct);
            throw new InvalidOperationException("bug");
        }

        private KAgentStageClient Real() =>
            new(factory.CreateClient(nameof(IStageAgentClient)), options,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<KAgentStageClient>.Instance);
    }
}
