using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Features.People;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Features.Passwords;

/// <summary>
/// D156: spend-then-set in one transaction under the account lock, as an invite accept. The conditional UPDATE lets the
/// link through once; the account is checked again under the lock (owner, disabled, Entra-managed while on: refused, and
/// the link revoked for good, D165); a rejected password rolls everything back, so the link still works. On success the password is
/// replaced, the lockout and failed count cleared, every other open link revoked, and every session ended (stamp + refresh
/// chains), all audited in the same transaction. No session is started.
/// </summary>
internal sealed class CompletePasswordResetHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, PasswordResets resets, SessionRevocation sessions, IdentityAudit audit,
    ClientAddress client, TimeProvider time, ILogger<CompletePasswordResetHandler> logger)
    : IRequestHandler<CompletePasswordResetCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(CompletePasswordResetCommand command, CancellationToken ct)
    {
        var hash = ResetTokens.Hash(command.Token);
        var now = time.GetUtcNow();
        var pending = await db.PasswordResets.AsNoTracking()
            .Where(r => r.TokenHash == hash && r.UsedUtc == null && r.RevokedUtc == null && r.ExpiresUtc > now)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { r.Id, r.UserId, r.CreatedBy, u.NormalizedEmail })
            .SingleOrDefaultAsync(ct);
        if (pending is null)
            return await UnknownAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLock.AcquireAsync(db, pending.NormalizedEmail!, ct);
        // The real gate: a concurrent reset, or a newer link revoking this one, may have spent it since the read above.
        var spent = await db.PasswordResets
            .Where(r => r.Id == pending.Id && r.UsedUtc == null && r.RevokedUtc == null && r.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedUtc, now), ct);
        if (spent == 0)
            return await InvalidAsync(transaction, pending.UserId, "no longer pending", ct);

        var user = (await users.FindByIdAsync(pending.UserId))!;
        await db.Entry(user).ReloadAsync(ct);
        if (await resets.RefusalAsync(user, ct) is { } refused)
        {
            // D165: the link dies here (revoked, in this transaction), so enabling the account later cannot revive it.
            await db.PasswordResets.Where(r => r.Id == pending.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedUtc, (DateTimeOffset?)null).SetProperty(r => r.RevokedUtc, now), ct);
            await RefusedAsync(null, user.Id, refused.Message + " The link was revoked.", ct);
            await transaction.CommitAsync(ct);
            return new Outcome<bool>(refused.Status, Message: refused.Message);
        }

        if (await users.HasPasswordAsync(user))
            LockedAccount.Require(await users.RemovePasswordAsync(user));
        var added = await users.AddPasswordAsync(user, command.Password);
        if (!added.Succeeded)
        {
            await RefusedAsync(transaction, user.Id, "rejected: " + string.Join(", ", added.Errors.Select(e => e.Code)), ct);
            throw new ValidationException(added.Errors.Select(e =>
                new ValidationFailure(e.Code.StartsWith("Password") ? nameof(CompletePasswordResetCommand.Password) : nameof(CompletePasswordResetCommand.Token), e.Description)));
        }
        LockedAccount.Require(await users.SetLockoutEndDateAsync(user, null));
        LockedAccount.Require(await users.ResetAccessFailedCountAsync(user));
        await resets.RevokeOpenAsync(user.Id, pending.Id, ct);
        await audit.WriteAsync(AuditActions.ResetCompleted, user.Id, user.Id, new { issuedBy = pending.CreatedBy ?? "email request" }, ct);
        await sessions.EndAllAsync(user, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Password of {UserId} reset from {RemoteIp} (link issued by {IssuedBy}); every session ended", user.Id, client.Current,
            pending.CreatedBy ?? "an email request");
        return Outcome<bool>.Ok(true);
    }

    /// <summary>A token that matches no open link: anyone can send one, so its row is sampled (D167).</summary>
    private async Task<Outcome<bool>> UnknownAsync(CancellationToken ct)
    {
        const string reason = "unknown, used, revoked or expired";
        logger.LogWarning("Password reset refused from {RemoteIp}: account {UserId}, {Reason}", client.Current, "unknown", reason);
        await audit.WriteSampledAsync(AuditActions.ResetRefused, "reset", null, null, new { reason, via = "reset link" }, ct);
        return Outcome<bool>.NotFound(PasswordResetStatusHandler.Invalid);
    }

    private async Task<Outcome<bool>> InvalidAsync(IDbContextTransaction? transaction, string? userId, string reason, CancellationToken ct)
    {
        await RefusedAsync(transaction, userId, reason, ct);
        return Outcome<bool>.NotFound(PasswordResetStatusHandler.Invalid);
    }

    /// <summary>Never the token or password. The refused change is rolled back first, so the row is written on its own and survives it.</summary>
    private async Task RefusedAsync(IDbContextTransaction? transaction, string? userId, string reason, CancellationToken ct)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(ct);
        logger.LogWarning("Password reset refused from {RemoteIp}: account {UserId}, {Reason}", client.Current, userId ?? "unknown", reason);
        await audit.WriteAsync(AuditActions.ResetRefused, null, userId, new { reason, via = "reset link" }, ct);
    }
}
