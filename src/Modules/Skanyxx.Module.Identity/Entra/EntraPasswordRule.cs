using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// D8: while the owner has Microsoft sign-in enabled, an Entra-managed account other than the owner signs in with
/// Microsoft only. Its password (if it has one) starts no session and its refresh tokens are refused, so a removal from
/// the mapped groups in Entra reaches the person at their next session instead of never. Keyed on the owner's
/// <see cref="EntraConfig.Enabled"/>, not on whether sign-in works (D14): a stored secret that can no longer be
/// decrypted turns Microsoft sign-in off, but must not hand managed accounts their passwords back. With it turned off
/// the password works again: the account must not be locked out by the owner turning the feature off. The switch is
/// read from the settings row, not the cache: another replica's cache lags a save by up to 30 s, and a password session
/// issued in that window would outlive the off → on stamp rotation (D15, QA-3 L2).
/// </summary>
internal sealed class EntraPasswordRule(AccountsDbContext db, UserManager<IdentityUser> users)
{
    public const string Message = "This account signs in with Microsoft.";

    public async Task<bool> RefusesAsync(IdentityUser user, CancellationToken ct) =>
        await db.EntraSettings.AnyAsync(s => s.Id == EntraSettings.SingletonId && s.Enabled, ct)
        && await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.LoginProvider == EntraScheme.Name, ct)
        && !await users.IsInRoleAsync(user, SkanyxxRoles.Owner);
}
