using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>Reads teams with their members: two queries whatever the count.</summary>
internal static class OrgRows
{
    public static async Task<IReadOnlyList<TeamDto>> TeamsAsync(AccountsDbContext db, IQueryable<OrgTeam> teams, CancellationToken ct)
    {
        var rows = await teams.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(ct);
        var slugs = rows.Select(t => t.Slug).ToList();
        var members = (await db.TeamMembers.AsNoTracking().Where(m => slugs.Contains(m.TeamSlug))
                .OrderBy(m => m.UserId).Select(m => new { m.TeamSlug, m.UserId }).ToListAsync(ct))
            .ToLookup(m => m.TeamSlug, m => m.UserId);
        return [.. rows.Select(t => new TeamDto(t.Slug, t.Name, t.DepartmentSlug, [.. members[t.Slug]]))];
    }

    public static async Task<TeamDto> TeamAsync(AccountsDbContext db, string slug, CancellationToken ct) =>
        (await TeamsAsync(db, db.Teams.Where(t => t.Slug == slug), ct)).Single();
}
