using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Org;

/// <summary>
/// <see cref="IOrgMembership"/> for other modules: one query per call, no cache, so an added or removed member or a
/// team moved to another department counts on the next request. Departments come through the teams.
/// </summary>
internal sealed class OrgMembershipReader(AccountsDbContext db) : IOrgMembership
{
    public async Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct)
    {
        var teams = await db.TeamMembers.AsNoTracking().Where(m => m.UserId == userId)
            .Join(db.Teams, m => m.TeamSlug, t => t.Slug, (_, t) => new { t.Slug, t.DepartmentSlug })
            .ToListAsync(ct);
        return new OrgMembership(teams.Select(t => t.Slug).ToHashSet(), teams.Select(t => t.DepartmentSlug).ToHashSet());
    }
}
