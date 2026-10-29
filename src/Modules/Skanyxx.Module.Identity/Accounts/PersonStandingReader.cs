using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary><see cref="IPersonStanding"/> for other modules: two queries per call, no cache.</summary>
internal sealed class PersonStandingReader(AccountsDbContext db) : IPersonStanding
{
    public async Task<IReadOnlySet<string>> ActiveRolesAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.LockoutEnd }).SingleOrDefaultAsync(ct);
        if (user is null || AccountStatus.IsDisabled(user.LockoutEnd))
            return new HashSet<string>();
        return (await db.UserRoles.Where(ur => ur.UserId == userId).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct)).ToHashSet();
    }
}
