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
/// Anyone who can see a workflow may copy it; the copy is theirs and shared with nobody. An allowance of chained writes
/// (D372) is not copied: it was given to that workflow by its saver, not to whoever copies it.
/// </summary>
internal sealed class DuplicateWorkflowHandler(
    InvestigateDbContext db, OrgTeams org, InvestigateAudit audit, IOptions<InvestigateOptions> options, TimeProvider clock)
    : IRequestHandler<DuplicateWorkflowCommand, Outcome<WorkflowDto>>
{
    public async Task<Outcome<WorkflowDto>> Handle(DuplicateWorkflowCommand command, CancellationToken ct)
    {
        var reach = await org.ReachAsync(command.Viewer, ct);
        var source = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(w => w.Id == command.Id, ct);
        if (source is null || !WorkflowAccess.CanSee(source, command.Viewer, reach))
            return Outcome<WorkflowDto>.NotFound("No such workflow.");
        if (await db.Workflows.CountAsync(w => w.OwnerId == command.Viewer.UserId, ct) >= options.Value.MaxWorkflowsPerUser)
            return SaveWorkflowHandler.TooMany(options.Value.MaxWorkflowsPerUser);

        var now = clock.GetUtcNow().UtcDateTime;
        var name = $"Copy of {source.Name}";
        var copy = new Workflow
        {
            Id = Guid.NewGuid(), OwnerId = command.Viewer.UserId, Name = name.Length > 120 ? name[..120] : name,
            Description = source.Description, Steps = source.Steps, CreatedAt = now, UpdatedAt = now
        };
        db.Workflows.Add(copy);
        await db.SaveChangesAsync(ct);

        await audit.WorkflowAsync(InvestigateAudit.WorkflowDuplicated, command.Viewer.UserId, copy.Id, copy.Name, WorkflowTree.Flatten(copy.Steps).Count(), null);
        return Outcome<WorkflowDto>.Created(WorkflowMapper.Full(copy, command.Viewer, reach));
    }
}
