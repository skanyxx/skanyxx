using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>A step has its own deadline: an agent that does not answer in time fails the run, and the rest is skipped.</summary>
public sealed class TimeoutTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    protected override void Configure(Dictionary<string, string?> settings) => settings["Investigate:StepTimeoutSeconds"] = "5";

    [Fact]
    public async Task AStepOverItsTimeout_FailsTheRun()
    {
        App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return "late"; };
        var workflow = await App.CreateAsync(Builder, "Slow", [Agent("s1", Researcher, "x", "Lookup"), Agent("s2", Analyst, "y")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal("Step 'Lookup' (kagent/researcher) failed: timed out after 5 s.", done.Failure);
        Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "s2").State);
        var lookup = done.StepRuns.Single(s => s.StepId == "s1");
        Assert.Equal(StepState.Failed, lookup.State);
        Assert.InRange((lookup.EndedAt!.Value - lookup.StartedAt!.Value).TotalSeconds, 4.5, 20);
    }
}
