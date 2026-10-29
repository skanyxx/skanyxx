using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class GetWorkflowHandler(InvestigateDbContext db, OrgTeams org) : IRequestHandler<GetWorkflowQuery, Outcome<WorkflowDto>>
{
    public async Task<Outcome<WorkflowDto>> Handle(GetWorkflowQuery query, CancellationToken ct)
    {
        var workflow = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(w => w.Id == query.Id, ct);
        return workflow is not null && WorkflowAccess.CanSee(workflow, query.Viewer, await org.MemberOfAsync(query.Viewer, ct))
            ? Outcome<WorkflowDto>.Ok(WorkflowMapper.Full(workflow, query.Viewer))
            : Outcome<WorkflowDto>.NotFound("No such workflow.");
    }
}
