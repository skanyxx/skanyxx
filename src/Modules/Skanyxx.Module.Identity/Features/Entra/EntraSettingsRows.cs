using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>Reads the stored settings for the owner: two queries, never the secret.</summary>
internal static class EntraSettingsRows
{
    public static async Task<EntraSettingsDto> ReadAsync(
        AccountsDbContext db, EntraSettingsCache cache, EntraRedirectUri redirect, EntraKeyRing keyRing, CancellationToken ct)
    {
        var row = await db.EntraSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var groups = await db.EntraGroups.AsNoTracking().OrderBy(g => g.GroupId).ToListAsync(ct);
        return new EntraSettingsDto(row?.Enabled ?? false, row?.TenantId ?? "", row?.ClientId ?? "", row?.ProtectedClientSecret is not null,
            cache.Current.CanSignIn, [.. groups.Select(g => new EntraGroupMapDto(g.GroupId, g.Label, g.Roles, g.Teams))],
            redirect.Callback, row?.UpdatedBy, row?.UpdatedUtc, row?.ProtectedClientSecret is not null && keyRing.Unencrypted);
    }
}
