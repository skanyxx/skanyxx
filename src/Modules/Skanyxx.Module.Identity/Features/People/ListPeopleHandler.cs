using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>Three queries whatever the head count (users, their roles, their display names).</summary>
internal sealed class ListPeopleHandler(AccountsDbContext db) : IRequestHandler<ListPeopleQuery, Outcome<IReadOnlyList<PersonDto>>>
{
    public async Task<Outcome<IReadOnlyList<PersonDto>>> Handle(ListPeopleQuery query, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).Select(u => new { u.Id, u.Email, u.LockoutEnd }).ToListAsync(ct);
        var roles = (await db.UserRoles.Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name }).ToListAsync(ct))
            .ToLookup(r => r.UserId, r => r.Name!);
        // Grouped, not ToDictionary: one stray duplicate claim must not take the whole page down.
        var names = (await db.UserClaims.AsNoTracking().Where(c => c.ClaimType == SkanyxxClaims.DisplayName)
                .OrderBy(c => c.Id).Select(c => new { c.UserId, c.ClaimValue }).ToListAsync(ct))
            .GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.First().ClaimValue);
        return Outcome<IReadOnlyList<PersonDto>>.Ok([.. users.Select(u => new PersonDto(
            u.Id, u.Email!, names.GetValueOrDefault(u.Id), [.. roles[u.Id].Order()], AccountStatus.IsDisabled(u.LockoutEnd)))]);
    }
}
