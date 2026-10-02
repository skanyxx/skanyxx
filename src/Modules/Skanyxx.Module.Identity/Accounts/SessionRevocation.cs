using Microsoft.AspNetCore.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Ends every session of a user: rotates the security stamp (cookies and access tokens are refused from their
/// next request) and drops the refresh chains. Sign-out, role changes and disabling all go through here.
/// </summary>
internal sealed class SessionRevocation(AccountsDbContext db, UserManager<IdentityUser> users, RefreshChains chains)
{
    public async Task EndAllAsync(IdentityUser user, CancellationToken ct)
    {
        await RotateStampAsync(user, ct);
        await chains.RevokeAllAsync(user.Id, ct);
    }

    /// <summary>
    /// A concurrent write to the user row (a failed sign-in, another sign-out) makes the update a concurrency failure;
    /// retry once on fresh values. Still failing means nothing was revoked, and the caller must not be told otherwise.
    /// </summary>
    private async Task RotateStampAsync(IdentityUser user, CancellationToken ct)
    {
        if ((await users.UpdateSecurityStampAsync(user)).Succeeded)
            return;

        await db.Entry(user).ReloadAsync(ct);
        var retry = await users.UpdateSecurityStampAsync(user);
        if (!retry.Succeeded)
            throw new InvalidOperationException(
                $"Could not rotate the security stamp of user {user.Id}: {string.Join(", ", retry.Errors.Select(e => e.Code))}.");
    }
}
