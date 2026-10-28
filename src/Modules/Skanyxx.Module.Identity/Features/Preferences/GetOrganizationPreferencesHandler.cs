using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class GetOrganizationPreferencesHandler(AccountsDbContext db) : IRequestHandler<GetOrganizationPreferencesQuery, Outcome<PreferenceSet>>
{
    public async Task<Outcome<PreferenceSet>> Handle(GetOrganizationPreferencesQuery query, CancellationToken ct) =>
        Outcome<PreferenceSet>.Ok(PreferenceJson.Read(await db.OrganizationPreferences.AsNoTracking().Select(p => p.Json).FirstOrDefaultAsync(ct)));
}
