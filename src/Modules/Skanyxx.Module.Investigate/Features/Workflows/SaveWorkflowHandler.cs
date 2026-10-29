using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

/// <summary>
/// Saves a workflow. A builder creates their own and edits only theirs; a team member who can only see a shared one
/// gets 403 (it is theirs to run, not to change). The team must be one the saver may share with, unless it is unchanged.
/// Chained writes are allowed only by the owner or a supervisor, and every save that allows them is audited (D372).
/// </summary>
internal sealed class SaveWorkflowHandler(
    InvestigateDbContext db, OrgTeams org, AgentCheck agents, InvestigateAudit audit, IOptions<InvestigateOptions> options, TimeProvider clock)
    : IRequestHandler<SaveWorkflowCommand, Outcome<WorkflowDto>>
{
    public async Task<Outcome<WorkflowDto>> Handle(SaveWorkflowCommand command, CancellationToken ct)
    {
        var viewer = command.Viewer;
        var reach = await org.ReachAsync(viewer, ct);
        Workflow? workflow = null;
        if (command.Id is { } id)
        {
            workflow = await db.Workflows.SingleOrDefaultAsync(w => w.Id == id, ct);
            if (workflow is null || !WorkflowAccess.CanSee(workflow, viewer, reach))
                return Outcome<WorkflowDto>.NotFound("No such workflow.");
            if (!WorkflowAccess.CanEdit(workflow, viewer, reach))
                return Outcome<WorkflowDto>.Forbidden("Only its owner or a supervisor can change this workflow; duplicate it to make your own.");
        }
        else if (await db.Workflows.CountAsync(w => w.OwnerId == viewer.UserId, ct) >= options.Value.MaxWorkflowsPerUser)
            return TooMany(options.Value.MaxWorkflowsPerUser);

        if (command.AllowChainedWrites && !viewer.IsSupervisor)
            return Outcome<WorkflowDto>.Forbidden("Only the owner or a supervisor can allow chained writes on a workflow.");

        if (command.SharedTeam is { } team && team != workflow?.SharedTeam
            && (await org.ShareableAsync(viewer, ct)).All(t => t.Slug != team))
            return Outcome<WorkflowDto>.Forbidden($"You cannot share with team '{team}'.");

        if (await agents.RefuseAsync<WorkflowDto>(command.Steps, command.AllowChainedWrites, ct) is { } refused)
            return refused;

        var now = clock.GetUtcNow().UtcDateTime;
        var created = workflow is null;
        workflow ??= db.Workflows.Add(new Workflow { Id = Guid.NewGuid(), OwnerId = viewer.UserId, CreatedAt = now }).Entity;
        workflow.Name = command.Name.Trim();
        workflow.Description = command.Description?.Trim() ?? "";
        workflow.SharedTeam = command.SharedTeam;
        workflow.Steps = command.Steps;
        workflow.AllowChainedWrites = command.AllowChainedWrites;
        workflow.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var steps = WorkflowTree.Flatten(workflow.Steps).Count();
        await audit.WorkflowAsync(created ? InvestigateAudit.WorkflowCreated : InvestigateAudit.WorkflowUpdated,
            viewer.UserId, workflow.Id, workflow.Name, steps, workflow.SharedTeam);
        if (workflow.AllowChainedWrites)
            await audit.WorkflowAsync(InvestigateAudit.ChainedWritesAllowed, viewer.UserId, workflow.Id, workflow.Name, steps, workflow.SharedTeam);
        var dto = WorkflowMapper.Full(workflow, viewer, reach);
        return created ? Outcome<WorkflowDto>.Created(dto) : Outcome<WorkflowDto>.Ok(dto);
    }

    public static Outcome<WorkflowDto> TooMany(int max) =>
        Outcome<WorkflowDto>.Conflict(null, $"You already have {max} workflows; delete one you no longer use first.", "too-many");
}
