using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Passwords;

/// <summary>
/// Who may reset a password (D157) and the links themselves (D156). Not the owner (break-glass is the bootstrap token,
/// unchanged), not a disabled account (enable it first), not an Entra-managed account while Microsoft sign-in is on (its
/// password starts no session then, D8). Everything here runs inside the caller's transaction, under the account lock.
/// </summary>
internal sealed class PasswordResets(AccountsDbContext db, UserManager<IdentityUser> users, EntraPasswordRule entra,
    IOptions<IdentityModuleOptions> options, TimeProvider time)
{
    public const string Owner = "The owner's password is not reset this way: sign in with the bootstrap token (break-glass).";

    public const string Disabled = "The account is disabled; enable it first.";

    /// <summary>Null when <paramref name="user"/> may reset; otherwise the status and why not.</summary>
    public async Task<(OutcomeStatus Status, string Message)?> RefusalAsync(IdentityUser user, CancellationToken ct)
    {
        if (await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
            return (OutcomeStatus.Forbidden, Owner);
        if (AccountStatus.IsDisabled(user))
            return (OutcomeStatus.Conflict, Disabled);
        if (await entra.RefusesAsync(user, ct))
            return (OutcomeStatus.Conflict, EntraPasswordRule.Message);
        return null;
    }

    /// <summary>Whether a link for <paramref name="userId"/> was made within <paramref name="window"/> (the self-service resend brake).</summary>
    public Task<bool> RecentAsync(string userId, TimeSpan window, CancellationToken ct)
    {
        var since = time.GetUtcNow() - window;
        return db.PasswordResets.AnyAsync(r => r.UserId == userId && r.CreatedUtc > since, ct);
    }

    /// <summary>A new link for the account; its older open one is revoked first (one open link per account).</summary>
    public async Task<(string Token, DateTimeOffset Expires)> IssueAsync(string userId, string? createdBy, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await RevokeOpenAsync(userId, null, ct);
        var token = ResetTokens.New();
        var reset = new PasswordReset
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            TokenHash = ResetTokens.Hash(token),
            CreatedBy = createdBy,
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(options.Value.PasswordResetMinutes)
        };
        db.PasswordResets.Add(reset);
        await db.SaveChangesAsync(ct);
        return (token, reset.ExpiresUtc);
    }

    public Task<int> RevokeOpenAsync(string userId, string? exceptId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.PasswordResets.Where(r => r.UserId == userId && r.Id != exceptId && r.UsedUtc == null && r.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedUtc, now), ct);
    }
}
