using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Invites;

/// <summary>
/// Spend-then-create in one transaction, under the email's account lock (taken before the invite row, the order
/// <see cref="CreateInviteHandler"/> uses): the conditional UPDATE lets exactly one accept through, and a later
/// failure (a rejected password) rolls the spend back so the link still works. Only a pending invite gets as far as the
/// lock, so an old link cannot keep someone's sign-ins busy.
/// </summary>
internal sealed class AcceptInviteHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionIssuer sessions, ClientAddress client, TimeProvider time,
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
            return Invalid(null, "unknown, used, revoked or expired");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLock.AcquireAsync(db, pending.NormalizedEmail, ct);
        // The real gate: a concurrent accept or revoke may have spent it since the read above.
        var spent = await db.Invites
            .Where(i => i.Id == pending.Id && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedUtc, now), ct);
        if (spent == 0)
            return Invalid(pending.Id, "no longer pending");

        var invite = await db.Invites.AsNoTracking().SingleAsync(i => i.Id == pending.Id, ct);
        if (await users.FindByEmailAsync(invite.Email) is not null)
        {
            LogRefused(invite.Id, "an account with the email exists");
            return Outcome<SignedIn>.Conflict(null, "An account with this email already exists; sign in instead.");
        }

        var user = new IdentityUser { UserName = invite.Email, Email = invite.Email };
        Require(await users.CreateAsync(user, command.Password), invite.Id);
        Require(await users.AddToRolesAsync(user, invite.Roles), invite.Id);
        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            Require(await users.AddClaimAsync(user, AccountReader.DisplayNameClaim(command.DisplayName.Trim())), invite.Id);
        await db.Invites.Where(i => i.Id == invite.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedUserId, user.Id), ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Invite {InviteId} (created by {InviterUserId}) accepted from {RemoteIp}: account {UserId} with roles {Roles}",
            invite.Id, invite.CreatedBy, client.Current, user.Id, invite.Roles);
        return Outcome<SignedIn>.Created(await sessions.StartAsync(user, command.UseCookie, ct));
    }

    private Outcome<SignedIn> Invalid(string? inviteId, string reason)
    {
        LogRefused(inviteId, reason);
        return Outcome<SignedIn>.NotFound(InviteStatusHandler.Invalid);
    }

    /// <summary>Never the token: the invite id when one was found, otherwise just "invalid".</summary>
    private void LogRefused(string? inviteId, string reason) =>
        logger.LogWarning("Invite accept refused from {RemoteIp}: invite {InviteId}, {Reason}", client.Current, inviteId ?? "invalid", reason);

    // Identity's own password rules report here; they become the usual 400 keyed by field.
    private void Require(IdentityResult result, string inviteId)
    {
        if (result.Succeeded)
            return;
        LogRefused(inviteId, "rejected: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        throw new ValidationException(result.Errors.Select(e =>
            new ValidationFailure(e.Code.StartsWith("Password") ? nameof(AcceptInviteCommand.Password) : nameof(AcceptInviteCommand.Token), e.Description)));
    }
}
