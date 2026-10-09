using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Email;

namespace Skanyxx.Module.Identity.Features.Status;

internal sealed class IdentityStatusHandler(AccountsDbContext db, LinkMail mail, InviteLinks links) : IRequestHandler<IdentityStatusQuery, Outcome<IdentityStatus>>
{
    public async Task<Outcome<IdentityStatus>> Handle(IdentityStatusQuery query, CancellationToken ct) =>
        Outcome<IdentityStatus>.Ok(new IdentityStatus(await db.Users.AnyAsync(ct), mail.Enabled && links.CanBuild));
}
