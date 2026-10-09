using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// D161: the account was refused by the Entra group re-check (no mapped group left, or gone from the tenant). A row in
/// <c>identity_user_tokens</c> (provider <c>entra</c>, never part of a principal) marks it, so the refusal acts once —
/// sessions ended, sandbox tasks stopped — and a retried revocation still knows the person's access is gone. Cleared when
/// the mapping grants access again or the Microsoft login is removed (under the account lock), and for every account when
/// a settings save leaves Microsoft sign-in unusable or changes the tenant (under the save lock). Honoured only while
/// Microsoft sign-in can be used: with it off the person signs in with a password again (D8).
/// </summary>
internal static class EntraRefusal
{
    public const string Name = "recheck_refused";

    public static Task<bool> IsMarkedAsync(AccountsDbContext db, string userId, CancellationToken ct) =>
        db.UserTokens.AnyAsync(t => t.UserId == userId && t.LoginProvider == EntraScheme.Name && t.Name == Name, ct);

    /// <summary>True when this call marked it (a fresh refusal); false when it already was.</summary>
    public static async Task<bool> MarkAsync(AccountsDbContext db, string userId, DateTimeOffset at, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_user_tokens ("UserId", "LoginProvider", "Name", "Value")
            VALUES ({userId}, {EntraScheme.Name}, {Name}, {at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)})
            ON CONFLICT DO NOTHING
            """, ct) == 1;

    /// <summary>Every account's mark, when the settings that refused them no longer apply (off, unmapped, another tenant).</summary>
    public static Task<int> ClearAllAsync(AccountsDbContext db, CancellationToken ct) =>
        db.UserTokens.Where(t => t.LoginProvider == EntraScheme.Name && t.Name == Name).ExecuteDeleteAsync(ct);

    public static Task ClearAsync(AccountsDbContext db, string userId, CancellationToken ct) =>
        db.UserTokens.Where(t => t.UserId == userId && t.LoginProvider == EntraScheme.Name && t.Name == Name).ExecuteDeleteAsync(ct);
}
