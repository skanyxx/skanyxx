using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class RerunHandler(InvestigateDbContext db, OrgTeams org, IMediator mediator) : IRequestHandler<RerunCommand, Outcome<RunDto>>
{
    public async Task<Outcome<RunDto>> Handle(RerunCommand command, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == command.Id, ct);
        return run is null || !WorkflowAccess.CanSee(run, command.Viewer, await org.ReachAsync(command.Viewer, ct))
            ? Outcome<RunDto>.NotFound("No such investigation run.")
            : await mediator.Send(new StartRunCommand(command.Viewer, run.WorkflowId, run.Input), ct);
    }
}
