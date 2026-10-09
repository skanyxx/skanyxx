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

namespace Skanyxx.Module.Identity.Features.Invites;

/// <summary>
/// Spend-then-create in one transaction, under the email's account lock (taken before the invite row, the order
/// <see cref="CreateInviteHandler"/> uses): the conditional UPDATE lets exactly one accept through, and a later
/// failure (a rejected password) rolls the spend back so the link still works. Only a pending invite gets as far as the
/// lock, so an old link cannot keep someone's sign-ins busy. The accept's audit row commits with the account; a refusal's
/// row is written after the rollback, so it stays while the refused change does not.
/// </summary>
internal sealed class AcceptInviteHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionIssuer sessions, IdentityAudit audit, ClientAddress client, TimeProvider time,
    ILogger<AcceptInviteHandler> logger)
    : IRequestHandler<AcceptInviteCommand, Outcome<SignedIn>>
{
    public async Task<Outcome<SignedIn>> Handle(AcceptInviteCommand command, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(command.Token);
        var now = time.GetUtcNow();
        var pending = await db.Invites.AsNoTracking()
            .Where(i => i.TokenHash == hash && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .Select(i => new { i.Id, i.NormalizedEmail })
            .SingleOrDefaultAsync(ct);
        if (pending is null)
            return await UnknownAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLock.AcquireAsync(db, pending.NormalizedEmail, ct);
        // The real gate: a concurrent accept or revoke may have spent it since the read above.
        var spent = await db.Invites
            .Where(i => i.Id == pending.Id && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedUtc, now), ct);
        if (spent == 0)
            return await InvalidAsync(transaction, pending.Id, "no longer pending", ct);

        var invite = await db.Invites.AsNoTracking().SingleAsync(i => i.Id == pending.Id, ct);
        if (await users.FindByEmailAsync(invite.Email) is not null)
        {
            await RefusedAsync(transaction, invite.Id, "an account with the email exists", ct);
            return Outcome<SignedIn>.Conflict(null, "An account with this email already exists; sign in instead.");
        }

        var user = new IdentityUser { UserName = invite.Email, Email = invite.Email };
        await RequireAsync(await users.CreateAsync(user, command.Password), transaction, invite.Id, ct);
        await RequireAsync(await users.AddToRolesAsync(user, invite.Roles), transaction, invite.Id, ct);
        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            await RequireAsync(await users.AddClaimAsync(user, AccountReader.DisplayNameClaim(command.DisplayName.Trim())), transaction, invite.Id, ct);
        await db.Invites.Where(i => i.Id == invite.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedUserId, user.Id), ct);
        await audit.WriteAsync(AuditActions.InviteAccepted, user.Id, invite.Id,
            new { createdBy = invite.CreatedBy, userId = user.Id, roles = invite.Roles }, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Invite {InviteId} (created by {InviterUserId}) accepted from {RemoteIp}: account {UserId} with roles {Roles}",
            invite.Id, invite.CreatedBy, client.Current, user.Id, invite.Roles);
        return Outcome<SignedIn>.Created(await sessions.StartAsync(user, command.UseCookie, ct));
    }

    /// <summary>A token that matches no open invite: anyone can send one, so its row is sampled (D167).</summary>
    private async Task<Outcome<SignedIn>> UnknownAsync(CancellationToken ct)
    {
        const string reason = "unknown, used, revoked or expired";
        logger.LogWarning("Invite accept refused from {RemoteIp}: invite {InviteId}, {Reason}", client.Current, "invalid", reason);
        await audit.WriteSampledAsync(AuditActions.InviteAcceptRefused, "invalid", null, null, new { reason }, ct);
        return Outcome<SignedIn>.NotFound(InviteStatusHandler.Invalid);
    }

    private async Task<Outcome<SignedIn>> InvalidAsync(IDbContextTransaction? transaction, string? inviteId, string reason, CancellationToken ct)
    {
        await RefusedAsync(transaction, inviteId, reason, ct);
        return Outcome<SignedIn>.NotFound(InviteStatusHandler.Invalid);
    }

    /// <summary>
    /// Never the token: the invite id when one was found, otherwise just "invalid". The refused change is rolled back
    /// first, so the row is written on its own and survives it.
    /// </summary>
    private async Task RefusedAsync(IDbContextTransaction? transaction, string? inviteId, string reason, CancellationToken ct)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(ct);
        logger.LogWarning("Invite accept refused from {RemoteIp}: invite {InviteId}, {Reason}", client.Current, inviteId ?? "invalid", reason);
        await audit.WriteAsync(AuditActions.InviteAcceptRefused, null, inviteId, new { reason }, ct);
    }

    // Identity's own password rules report here; they become the usual 400 keyed by field.
    private async Task RequireAsync(IdentityResult result, IDbContextTransaction transaction, string inviteId, CancellationToken ct)
    {
        if (result.Succeeded)
            return;
        await RefusedAsync(transaction, inviteId, "rejected: " + string.Join(", ", result.Errors.Select(e => e.Code)), ct);
        throw new ValidationException(result.Errors.Select(e =>
            new ValidationFailure(e.Code.StartsWith("Password") ? nameof(AcceptInviteCommand.Password) : nameof(AcceptInviteCommand.Token), e.Description)));
    }
}
