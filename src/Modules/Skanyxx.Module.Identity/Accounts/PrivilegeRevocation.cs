using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Publishes <see cref="PrivilegesRevoked"/> once the change is committed. Not cancellable: the change is saved, so
/// its follow-up must not depend on the owner's browser staying connected. A failing handler is logged at Error and
/// rethrown as a <see cref="RevocationFailedException"/> (the caller gets a 500 saying what did not happen); the log says what publishes again. A change that owes a revocation marks it in its
/// own transaction (<see cref="MarkAsync"/>, D13); <see cref="SettleAsync"/> publishes after the commit and clears the
/// mark only once the publish succeeded, so the next save or Microsoft sign-in of the account retries a failed one.
/// </summary>
internal sealed class PrivilegeRevocation(
    AccountsDbContext db, IPublisher publisher, EntraSettingsCache entra, TimeProvider time, ILogger<PrivilegeRevocation> logger)
{
    /// <summary>The reason when only an earlier, failed revocation is published again.</summary>
    public const string Retried = "an earlier revocation retried";

    public const string SaveAgain = "saving the same change again, or the next Microsoft sign-in of this account, publishes again";

    /// <summary>Inside the caller's transaction, under the account lock: the account is owed a revocation.</summary>
    public Task MarkAsync(string userId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO identity_pending_revocations ("UserId", "Generation", "MarkedUtc") VALUES ({userId}, {Guid.NewGuid()}, {time.GetUtcNow()})
        ON CONFLICT ("UserId") DO UPDATE SET "Generation" = EXCLUDED."Generation", "MarkedUtc" = EXCLUDED."MarkedUtc"
        """, ct);

    /// <summary>
    /// Inside the caller's transaction, under the account lock: the account keeps or regains supervisor, so what it
    /// issued is its own again and an owed revocation is dropped. Left in place, a stale mark would revoke the secrets
    /// issued after the re-grant (D17).
    /// </summary>
    public Task ForgiveAsync(string userId, CancellationToken ct) =>
        db.PendingRevocations.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

    /// <summary>
    /// After the commit: publishes when a revocation is owed, or whenever <paramref name="always"/> (a People save,
    /// D089), then clears the mark it saw. A mark made meanwhile has another generation and stays.
    /// </summary>
    public async Task SettleAsync(string userId, string reason, string actorId, string retry, bool always = false)
    {
        var owed = await db.PendingRevocations.Where(p => p.UserId == userId).Select(p => (Guid?)p.Generation)
            .SingleOrDefaultAsync(CancellationToken.None);
        if (owed is null && !always)
            return;

        await PublishAsync(userId, reason, actorId, retry);
        if (owed is { } generation)
            await db.PendingRevocations.Where(p => p.UserId == userId && p.Generation == generation).ExecuteDeleteAsync(CancellationToken.None);
    }

    /// <summary>
    /// Whether the account is disabled, or refused by the Entra re-check (<see cref="EntraRefusal"/>), is read now, not
    /// passed in: a retried revocation of such an account must still stop what runs on its behalf (D154, D161), and an
    /// enabled, mapped one must not. A refusal counts only while Microsoft sign-in can be used: with it off the person
    /// signs in with a password again (D8), so a mark a settings save has not cleared yet must not stop their tasks. A failure is rethrown as a <see cref="RevocationFailedException"/> carrying what did
    /// not happen, for the owner (D162).
    /// </summary>
    private async Task PublishAsync(string userId, string reason, string actorId, string retry)
    {
        try
        {
            var lockoutEnd = await db.Users.Where(u => u.Id == userId).Select(u => u.LockoutEnd).SingleOrDefaultAsync(CancellationToken.None);
            var disabled = AccountStatus.IsDisabled(lockoutEnd);
            var removed = disabled || (entra.Current.CanSignIn && await EntraRefusal.IsMarkedAsync(db, userId, CancellationToken.None));
            await publisher.Publish(new PrivilegesRevoked(userId, reason, disabled, removed), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Revoking what {UserId} issued ({Reason}) failed after {ActorUserId}'s change was saved; {Retry}",
                userId, reason, actorId, retry);
            throw new RevocationFailedException(Describe(ex), ex);
        }
    }

    /// <summary>The handlers' own words where they gave them (<see cref="RevocationFailedException"/>), else a plain one.</summary>
    private static string Describe(Exception ex)
    {
        var failures = ex is AggregateException aggregate ? aggregate.InnerExceptions : [ex];
        return string.Join(" ", failures.Select(f => f is RevocationFailedException own ? own.Message : "Not everything the person issued could be revoked.").Distinct());
    }
}
