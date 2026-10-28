using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;
using Skanyxx.Module.Tickets.Tests.Infrastructure;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>requirements.md §3.2: a finished run tells memory once, with a short outcome; a cancelled one does not.</summary>
public sealed class RunFinishedTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    private sealed class Recorder : INotificationHandler<TicketRunFinished>
    {
        public ConcurrentQueue<TicketRunFinished> Seen { get; } = new();

        public Task Handle(TicketRunFinished notification, CancellationToken ct)
        {
            Seen.Enqueue(notification);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnApprovedRun_IsPublishedOnce_AsSucceeded_ForItsOwner()
    {
        var recorder = new Recorder();
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            services => services.AddSingleton<INotificationHandler<TicketRunFinished>>(recorder));

        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        await App.DecideAsync(run.Id, "approve");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        var finished = Assert.Single(recorder.Seen);
        Assert.Equal(run.Id, finished.RunId);
        Assert.Equal(TicketsApp.User, finished.OwnerUserId);
        Assert.Equal("SDB-1", finished.TicketKey);
        Assert.True(finished.Succeeded);
        Assert.InRange(finished.Outcome.Length, 1, RunFinishedNotice.OutcomeChars);
    }

    [Fact]
    public async Task ARunTheWorkerFailsOnInternalErrors_IsPublishedAsFailed()
    {
        // C-6: RunStepper.FailAsync ends the run with a direct update; memory must still hear of it.
        var recorder = new Recorder();
        RobustnessTests.ExplodingOn.Calls = 0;
        RobustnessTests.ExplodingOn.FailFirst = int.MaxValue;
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url, services =>
        {
            services.AddScoped<IStageAgentClient, RobustnessTests.ExplodingOn>();
            services.AddSingleton<INotificationHandler<TicketRunFinished>>(recorder);
        });
        await App.SavePipelineAsync("boom", Stages.Pipeline(Stages.Stage("plan", "plan", "ticket-boom")));

        var run = await App.StartRunAsync(pipelineId: "boom");
        await App.WaitForAsync(run.Id, RunState.Failed);

        var finished = Assert.Single(recorder.Seen);
        Assert.Equal(run.Id, finished.RunId);
        Assert.False(finished.Succeeded);
        Assert.Equal(TicketsApp.User, finished.OwnerUserId);
        Assert.Contains("internal error", finished.Outcome);
    }

    [Fact]
    public void OnlyASucceededOrFailedRun_IsFinishedWork()
    {
        var ticket = new Ticket("SDB-7", "Fix refunds, see SDB-3 and sdb-7", "Also SDB-9.", "Bug", "Open", "High", ["billing"], null,
            "https://jira.example/browse/SDB-7", null);
        Run Make(RunState state) => new()
        {
            TicketKey = "SDB-7", Ticket = ticket, PipelineId = "p", PipelineName = "Pipeline", CreatedBy = "ana", State = state,
            StageRuns = [new StageRun { Id = 1, StageId = "s", Title = "Review", Output = new string('x', 5_000), Verdict = Verdict.Fail }]
        };

        Assert.Null(RunFinishedNotice.From(Make(RunState.Cancelled)));
        Assert.Null(RunFinishedNotice.From(Make(RunState.AwaitingHuman)));
        var failed = RunFinishedNotice.From(Make(RunState.Failed))!;
        Assert.False(failed.Succeeded);
        Assert.Equal(["SDB-3", "SDB-9"], failed.RelatedTickets);
        Assert.Equal(RunFinishedNotice.OutcomeChars, failed.Outcome.Length);
        Assert.StartsWith("Review (fail): ", failed.Outcome);
    }
}
