using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

internal sealed class GetEntraSettingsHandler(AccountsDbContext db, EntraSettingsCache cache, EntraRedirectUri redirect, EntraKeyRing keyRing)
    : IRequestHandler<GetEntraSettingsQuery, Outcome<EntraSettingsDto>>
{
    public async Task<Outcome<EntraSettingsDto>> Handle(GetEntraSettingsQuery query, CancellationToken ct) =>
        Outcome<EntraSettingsDto>.Ok(await EntraSettingsRows.ReadAsync(db, cache, redirect, keyRing, ct));
}
