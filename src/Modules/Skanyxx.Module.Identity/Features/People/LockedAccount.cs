using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>
/// Takes the person's account lock (the one sign-in holds while it writes the failed-attempt count), then loads them,
/// so a change is made on the row as it is now. Reloaded: the request may have tracked this user before the lock (the
/// caller's own authentication does), and a failed sign-in committed since would make the update a concurrency failure.
/// Null: no such person. Must run inside a transaction.
/// </summary>
internal static class LockedAccount
{
    public static async Task<IdentityUser?> LoadAsync(AccountsDbContext db, UserManager<IdentityUser> users, string userId, CancellationToken ct)
    {
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct);
        if (email is null)
            return null;

        await AccountLock.AcquireAsync(db, email, ct);
        var user = await users.FindByIdAsync(userId);
        if (user is not null)
            await db.Entry(user).ReloadAsync(ct);
        return user;
    }

    public static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Account update failed: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
    }
}
