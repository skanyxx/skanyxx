using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// A signed-in person proves they know their password again (D10), under the same rules as a sign-in: the account's
/// lock, the lockout (a locked-out account is refused unchecked), and a wrong password counts toward it. A session
/// cookie alone — stolen, or a browser left open — is then not enough for what needs this. Like a sign-in it does not
/// queue on the lock (CR m2): a check that finds another one in flight is refused unchecked and uncounted, so parallel
/// requests cannot pin a pooled connection each.
/// </summary>
internal sealed class PasswordStepUp(AccountsDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
{
    public const string Wrong = "That is not your current password.";

    public const string LockedOut = "Too many wrong passwords: try again later.";

    public const string Busy = "Another password check for this account is in progress; try again.";

    /// <summary>Null when the password is right; otherwise the status and why not.</summary>
    public async Task<(OutcomeStatus Status, string Message)?> CheckAsync(string userId, string password, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.Users.Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct) is not { } email)
            return (OutcomeStatus.Forbidden, Wrong);
        if (!await AccountLock.TryAcquireAsync(db, email, ct))
            return (OutcomeStatus.RateLimited, Busy);

        var user = (await users.FindByIdAsync(userId))!;
        await db.Entry(user).ReloadAsync(ct);
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        await transaction.CommitAsync(ct);
        return result.Succeeded ? null : (OutcomeStatus.Forbidden, result.IsLockedOut ? LockedOut : Wrong);
    }
}
