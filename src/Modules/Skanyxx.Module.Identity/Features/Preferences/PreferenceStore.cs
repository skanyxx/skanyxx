using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>Reads a person's and the organization's preference rows in one round trip.</summary>
internal static class PreferenceStore
{
    public static async Task<PreferencesView> ViewAsync(AccountsDbContext db, string userId, CancellationToken ct)
    {
        var own = await db.UserPreferences.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Json).FirstOrDefaultAsync(ct);
        var org = await db.OrganizationPreferences.AsNoTracking().Select(p => p.Json).FirstOrDefaultAsync(ct);
        return View(PreferenceJson.Read(own), PreferenceJson.Read(org));
    }

    public static PreferencesView View(PreferenceSet own, PreferenceSet org) => new(own, org, EffectivePreferences.Resolve(org, own));
}
