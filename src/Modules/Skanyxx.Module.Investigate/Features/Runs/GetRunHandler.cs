using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>A run with its steps, for its starter, the owner and a supervisor of the starter's team (D371); "not found" to anyone else.</summary>
internal sealed class GetRunHandler(InvestigateDbContext db, OrgTeams org) : IRequestHandler<GetRunQuery, Outcome<InvestigationRun>>
{
    public async Task<Outcome<InvestigationRun>> Handle(GetRunQuery query, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.StepRuns).SingleOrDefaultAsync(r => r.Id == query.Id, ct);
        return run is not null && WorkflowAccess.CanSee(run, query.Viewer, await org.ReachAsync(query.Viewer, ct))
            ? Outcome<InvestigationRun>.Ok(run)
            : Outcome<InvestigationRun>.NotFound("No such investigation run.");
    }
}
