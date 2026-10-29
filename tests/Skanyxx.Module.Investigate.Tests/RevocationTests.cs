using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>
/// D395: a run calls agents on its starter's authority, so a starter disabled or left below builder stops their runs:
/// the revocation ends the running and queued ones, and the worker asks identity again at pickup and before each step.
/// </summary>
public sealed class RevocationTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private const string StarterRefused = "The person who started this run may no longer run investigations (disabled, or no longer a builder).";

    protected override void Configure(Dictionary<string, string?> settings) => settings["Investigate:MaxConcurrentRuns"] = "1";

    private async Task RevokeAsync(string userId)
    {
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new PrivilegesRevoked(userId, "roles saved without supervisor"), Ct);
    }

    [Theory]
    [InlineData(SkanyxxRoles.Employee)]
    [InlineData(null)] // disabled
    public async Task Revocation_BelowBuilder_CancelsTheRunningAndTheQueuedRuns(string? roleNow)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Agents.Answers[Researcher] = async (_, ct) => { entered.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(30), ct); return "never"; };
        var workflow = await App.CreateAsync(Builder, "Long", [Agent("s1", Researcher, "x"), Agent("s2", Analyst, "y")]);
        var running = await App.StartAsync(Builder, workflow.Id, "first");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var queued = await App.StartAsync(Builder, workflow.Id, "second");
        Assert.Equal(RunState.Queued, (await App.GetRunAsync(queued.Id)).State);

        App.Org.RolesNow[Builder] = roleNow is null ? [] : [roleNow];
        await RevokeAsync(Builder);
        var stopped = await App.WaitForAsync(running.Id, RunState.Cancelled);
        var dropped = await App.GetRunAsync(queued.Id);

        Assert.Equal(StepState.Cancelled, stopped.StepRuns.Single(s => s.StepId == "s1").State);
        Assert.Equal(RunState.Cancelled, dropped.State);
        Assert.Contains("may no longer run investigations (roles saved without supervisor)", dropped.Failure);
        Assert.All(dropped.StepRuns, s => Assert.Equal(StepState.Skipped, s.State));
        Assert.DoesNotContain(App.Agents.Calls, c => c.Agent == Analyst);
    }

    [Fact]
    public async Task Revocation_OfASupervisorLeftABuilder_KeepsTheirRuns()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Agents.Answers[Researcher] = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return "found"; };
        var workflow = await App.CreateAsync(Supervisor, "Mine", [Agent("s1", Researcher, "x")]);
        var run = await App.StartAsync(Supervisor, workflow.Id, "x");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        App.Org.RolesNow[Supervisor] = [SkanyxxRoles.Builder];
        await RevokeAsync(Supervisor);
        release.SetResult();

        Assert.Equal(RunState.Succeeded, (await App.WaitForAsync(run.Id, RunState.Succeeded, RunState.Cancelled, RunState.Failed)).State);
    }

    /// <summary>A start let in just before the role was saved away: the revocation found nothing, the pickup does.</summary>
    [Fact]
    public async Task AStarterNoLongerABuilder_AtPickup_RunsNoStep()
    {
        var workflow = await App.CreateAsync(Builder, "Late", [Agent("s1", Researcher, "x")]);
        App.Org.RolesNow[Builder] = [SkanyxxRoles.Employee];

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Failed, RunState.Succeeded);

        Assert.Equal(StarterRefused, done.Failure);
        Assert.Empty(App.Agents.Calls);
    }

    [Fact]
    public async Task AStarterNoLongerABuilder_BeforeTheNextStep_StopsTheRun()
    {
        App.Agents.Answers[Researcher] = (_, _) =>
        {
            App.Org.RolesNow[Builder] = [SkanyxxRoles.Employee];
            return Task.FromResult("found");
        };
        var workflow = await App.CreateAsync(Builder, "Two", [Agent("s1", Researcher, "x"), Agent("s2", Analyst, "{{steps.s1}}")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Failed, RunState.Succeeded);

        Assert.Equal(StarterRefused, done.Failure);
        Assert.Equal(StepState.Succeeded, done.StepRuns.Single(s => s.StepId == "s1").State);
        Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "s2").State);
        Assert.DoesNotContain(App.Agents.Calls, c => c.Agent == Analyst);
    }
}
