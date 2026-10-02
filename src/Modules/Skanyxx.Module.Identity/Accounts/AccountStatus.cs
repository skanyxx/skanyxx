using Microsoft.AspNetCore.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Disabled = locked out until <see cref="DisabledUntil"/> (D026 slice, decision D4): sign-in already refuses a
/// locked-out account with the generic answer, so no new check is needed anywhere. A failed-attempt lockout ends
/// minutes from now and never reaches that date, so the two cannot be confused.
/// </summary>
internal static class AccountStatus
{
    public static readonly DateTimeOffset DisabledUntil = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsDisabled(DateTimeOffset? lockoutEnd) => lockoutEnd >= DisabledUntil;

    public static bool IsDisabled(IdentityUser user) => IsDisabled(user.LockoutEnd);
}
