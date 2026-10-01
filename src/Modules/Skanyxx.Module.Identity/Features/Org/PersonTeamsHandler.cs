using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class PersonTeamsHandler(AccountsDbContext db) : IRequestHandler<PersonTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>
{
    public async Task<Outcome<IReadOnlyList<TeamDto>>> Handle(PersonTeamsQuery query, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == query.UserId, ct))
            return Outcome<IReadOnlyList<TeamDto>>.NotFound("No person has that id.");

        var teams = db.Teams.Where(t => db.TeamMembers.Any(m => m.TeamSlug == t.Slug && m.UserId == query.UserId));
        return Outcome<IReadOnlyList<TeamDto>>.Ok(await OrgRows.TeamsAsync(db, teams, ct));
    }
}
