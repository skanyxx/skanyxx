using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

internal sealed class EntraStatusHandler(AccountsDbContext db, EntraSettingsCache cache) : IRequestHandler<EntraStatusQuery, Outcome<EntraStatus>>
{
    public async Task<Outcome<EntraStatus>> Handle(EntraStatusQuery query, CancellationToken ct) =>
        Outcome<EntraStatus>.Ok(new EntraStatus(cache.Current.CanSignIn,
            query.UserId is not null && await db.UserLogins.AnyAsync(l => l.UserId == query.UserId && l.LoginProvider == EntraScheme.Name, ct)));
}
