using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class ListInvitesHandler(AccountsDbContext db, TimeProvider time)
    : IRequestHandler<ListInvitesQuery, Outcome<IReadOnlyList<PendingInvite>>>
{
    public async Task<Outcome<IReadOnlyList<PendingInvite>>> Handle(ListInvitesQuery query, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var invites = await db.Invites.AsNoTracking()
            .Where(i => i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .OrderBy(i => i.CreatedUtc)
            .Select(i => new PendingInvite(i.Id, i.Email, i.Roles, i.CreatedBy, i.CreatedUtc, i.ExpiresUtc))
            .ToListAsync(ct);
        return Outcome<IReadOnlyList<PendingInvite>>.Ok(invites);
    }
}
