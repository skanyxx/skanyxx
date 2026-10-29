using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Engine;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>
/// Queues a run of a workflow the caller can see: a copy of its steps, the input, and one pending row per step, then
/// wakes the worker. Each run costs model turns, so one person and everyone together have caps, counted under one lock.
/// </summary>
internal sealed class StartRunHandler(
    InvestigateDbContext db, OrgTeams org, AgentCheck agents, RunSignal signal, InvestigateAudit audit, IOptions<InvestigateOptions> options)
    : IRequestHandler<StartRunCommand, Outcome<RunDto>>
{
    // All advisory-lock keys are listed in one place: Skanyxx.Module.Memory's MemoryMigrator.
    private const int StartLockKey = 0x494E5601;

    public async Task<Outcome<RunDto>> Handle(StartRunCommand command, CancellationToken ct)
    {
        var viewer = command.Viewer;
        var workflow = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(w => w.Id == command.WorkflowId, ct);
        if (workflow is null || !WorkflowAccess.CanSee(workflow, viewer, await org.ReachAsync(viewer, ct)))
            return Outcome<RunDto>.NotFound("No such workflow.");
        if (await agents.RefuseAsync<RunDto>(workflow.Steps, workflow.AllowChainedWrites, ct) is { } refused)
            return refused;

        var run = new InvestigationRun
        {
            Id = Guid.NewGuid(), WorkflowId = workflow.Id, WorkflowName = workflow.Name, Steps = workflow.Steps, Input = command.Input,
            State = RunState.Queued, CreatedBy = viewer.UserId,
            StepRuns = [.. WorkflowTree.Flatten(workflow.Steps).Select(n => new StepRun { StepId = n.Step.Id, State = StepState.Pending })]
        };

        var limits = options.Value;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({StartLockKey}, 0)", ct);
            var unfinished = db.Runs.Where(r => r.State == RunState.Queued || r.State == RunState.Running);
            var mine = await unfinished.CountAsync(r => r.CreatedBy == viewer.UserId, ct);
            if (mine >= limits.MaxActiveRunsPerUser)
                return Outcome<RunDto>.RateLimited($"You already have {mine} investigations running or queued; wait for one to finish or cancel it.");
            if (await unfinished.CountAsync(ct) >= limits.MaxActiveRuns)
                return Outcome<RunDto>.RateLimited("Too many investigations are running or queued; try again in a few minutes.");
            run.CreatedAt = await DbClock.NowAsync(db, ct);
            db.Runs.Add(run);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        signal.Notify();
        await audit.RunAsync(InvestigateAudit.RunStarted, viewer.UserId, run.Id, workflow.Id, workflow.Name, "queued");
        return Outcome<RunDto>.Accepted(RunMapper.Full(run, viewer));
    }
}
