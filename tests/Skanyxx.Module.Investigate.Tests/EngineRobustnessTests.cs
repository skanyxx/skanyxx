using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>
/// D373: a step that cannot be recorded fails its run at once, a run's agent steps share a few kagent slots, the stale
/// sweep reads the database's clock and ends steps as a run's end does, answers are redacted before they are kept, and a
/// person has a cap on workflows.
/// </summary>
public sealed class EngineRobustnessTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    protected override void Configure(Dictionary<string, string?> settings)
    {
        settings["Investigate:MaxParallelSteps"] = "2";
        settings["Investigate:MaxWorkflowsPerUser"] = "3";
    }

    [Fact]
    public async Task AStepWhoseRowCannotBeWritten_FailsTheRunAtOnce_AndItsSiblingIsCancelled()
    {
        await using (var db = Postgres.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE investigate_step_runs ADD CONSTRAINT test_no_boom CHECK (output IS NULL OR output NOT LIKE '%BOOM%')", Ct);
        try
        {
            App.Agents.Answers[Analyst] = (_, _) => Task.FromResult("BOOM");
            App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(25), ct); return "never"; };
            var workflow = await App.CreateAsync(Builder, "Unwritable",
                [Group("g1", GroupMode.Parallel, Agent("slow", Researcher, "x"), Agent("boom", Analyst, "y", "Boom")), Agent("after", Writer, "z")]);
            var started = DateTime.UtcNow;

            var done = await App.WaitForAsync((await App.StartAsync(Builder, workflow.Id, "x")).Id, RunState.Failed);

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "the sibling ran to its own end");
            Assert.StartsWith("Step 'Boom' stopped on an internal error", done.Failure);
            Assert.Equal(StepState.Cancelled, done.StepRuns.Single(s => s.StepId == "slow").State);
            Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "after").State);
            Assert.DoesNotContain(App.Agents.Calls, c => c.Agent == Writer);
        }
        finally
        {
            await using var db = Postgres.CreateDbContext();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE investigate_step_runs DROP CONSTRAINT IF EXISTS test_no_boom", CancellationToken.None);
        }
    }

    [Fact]
    public async Task AParallelGroup_CallsAtMostMaxParallelStepsAgentsAtOnce()
    {
        var now = 0;
        var peak = 0;
        async Task<string> Counted(string prompt, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref now);
            InterlockedMax(ref peak, current);
            await Task.Delay(300, ct);
            Interlocked.Decrement(ref now);
            return prompt;
        }
        App.Agents.Answers[Researcher] = Counted;
        App.Agents.Answers[Analyst] = Counted;
        App.Agents.Answers[Writer] = Counted;
        var workflow = await App.CreateAsync(Builder, "Wide",
            [Group("g1", GroupMode.Parallel, Agent("a", Researcher, "a"), Agent("b", Analyst, "b"), Agent("c", Writer, "c"), Agent("d", Researcher, "d"))]);

        var done = await App.WaitForAsync((await App.StartAsync(Builder, workflow.Id, "x")).Id, RunState.Succeeded, RunState.Failed);

        Assert.Equal(RunState.Succeeded, done.State);
        Assert.Equal(4, App.Agents.Calls.Count);
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task AStaleRun_IsFailedByTheDatabasesClock_RunFirst_ThenItsSteps()
    {
        var id = Guid.NewGuid();
        await using (var db = Postgres.CreateDbContext())
        {
            db.Runs.Add(new InvestigationRun
            {
                Id = id, WorkflowId = Guid.NewGuid(), WorkflowName = "Orphan", Steps = [Agent("s1", Researcher, "x"), Agent("s2", Analyst, "y")],
                Input = "x", State = RunState.Running, CreatedBy = Builder, CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow,
                StepRuns = [new StepRun { StepId = "s1", State = StepState.Running }, new StepRun { StepId = "s2", State = StepState.Pending }]
            });
            await db.SaveChangesAsync(Ct);
            // Stamped by the database itself, an hour behind: no replica's clock is involved.
            await db.Database.ExecuteSqlAsync($"UPDATE investigate_runs SET heartbeat_at = now() - interval '1 hour' WHERE id = {id}", Ct);
        }

        var done = await App.WaitForAsync(id, RunState.Failed);

        Assert.Equal("The server running this investigation stopped; run it again.", done.Failure);
        Assert.NotNull(done.EndedAt);
        Assert.Equal(StepState.Cancelled, done.StepRuns.Single(s => s.StepId == "s1").State);
        Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "s2").State);
    }

    [Fact]
    public async Task AnswersAndFailures_AreRedacted_BeforeTheyAreKept_OrReadByTheNextStep()
    {
        const string token = "gh" + "p_abcdefghijklmnopqrstuvwxyz0123456789AB";
        App.Agents.Answers[Researcher] = (_, _) => Task.FromResult($"Found it: {token}");
        var workflow = await App.CreateAsync(Builder, "Leaky", [Agent("s1", Researcher, "x"), Agent("s2", Analyst, "{{steps.s1}}")]);

        var done = await App.WaitForAsync((await App.StartAsync(Builder, workflow.Id, "x")).Id, RunState.Succeeded);
        var report = await App.Client(Builder).GetStringAsync($"/api/investigate/runs/{done.Id}/report.json", Ct);

        Assert.All(done.StepRuns, s => Assert.DoesNotContain(token, s.Output ?? ""));
        Assert.Contains("[redacted]", done.StepRuns.Single(s => s.StepId == "s1").Output);
        Assert.DoesNotContain(token, App.Agents.Calls.Single(c => c.Agent == Analyst).Prompt);
        Assert.DoesNotContain(token, report);
        Assert.DoesNotContain(token, Assert.Single(App.Cards.Investigations).Finding);
    }

    [Fact]
    public async Task APerson_HasACapOnWorkflows_CreatingAndDuplicating()
    {
        WorkflowStep[] steps = [Agent("s1", Researcher, "x")];
        var first = await App.CreateAsync(Builder, "1", steps);
        await App.CreateAsync(Builder, "2", steps);
        await App.CreateAsync(Builder, "3", steps);

        var fourth = await App.Client(Builder).PostAsJsonAsync("/api/investigate/workflows", new { name = "4", description = "", steps }, Json, Ct);
        var copy = await App.Client(Builder).PostAsync($"/api/investigate/workflows/{first.Id}/duplicate", null, Ct);
        var edit = await App.Client(Builder).PutAsJsonAsync($"/api/investigate/workflows/{first.Id}", new { name = "1b", description = "", steps }, Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Contains("3 workflows", await fourth.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, copy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        await App.CreateAsync(OtherBuilder, "Ben's own", steps);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
