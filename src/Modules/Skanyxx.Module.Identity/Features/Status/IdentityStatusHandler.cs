using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Status;

internal sealed class IdentityStatusHandler(AccountsDbContext db) : IRequestHandler<IdentityStatusQuery, Outcome<IdentityStatus>>
{
    public async Task<Outcome<IdentityStatus>> Handle(IdentityStatusQuery query, CancellationToken ct) =>
        Outcome<IdentityStatus>.Ok(new IdentityStatus(await db.Users.AnyAsync(ct)));
}
