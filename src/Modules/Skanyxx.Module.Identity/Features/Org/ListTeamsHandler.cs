using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class ListTeamsHandler(AccountsDbContext db) : IRequestHandler<ListTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>
{
    public async Task<Outcome<IReadOnlyList<TeamDto>>> Handle(ListTeamsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<TeamDto>>.Ok(await OrgRows.TeamsAsync(db, db.Teams, ct));
}
