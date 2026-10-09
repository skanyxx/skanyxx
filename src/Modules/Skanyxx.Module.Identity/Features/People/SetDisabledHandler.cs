using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>
/// Disable = locked out until <see cref="AccountStatus.DisabledUntil"/> + every session ended; enable = lockout and
/// failed count cleared. The owner is never disabled: there is no one left to enable them. Every disable (a repeat
/// one included, which makes it the retry) also revokes, after the commit, the durable credentials the person issued
/// (<see cref="PrivilegeRevocation"/>); enabling does not bring them back. A disable marks the revocation as owed in its
/// transaction (D13): a failed publish is retried by the next disable, enable, role save or Microsoft sign-in. Each is
/// audited in its transaction (D152); a disable also revokes the person's open password-reset links in it (D165) and
/// stops the person's sandbox tasks through that publish (D154). When that publish fails the change stays saved and
/// the <see cref="RevocationFailedException"/> says what did not happen and that disabling again retries (D162).
/// </summary>
internal sealed class SetDisabledHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionRevocation sessions, AccountReader accounts, PrivilegeRevocation revocation,
    PasswordResets resets, IdentityAudit audit, ClientAddress client, ILogger<SetDisabledHandler> logger)
    : IRequestHandler<SetDisabledCommand, Outcome<PersonDto>>
{
    public async Task<Outcome<PersonDto>> Handle(SetDisabledCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockedAccount.LoadAsync(db, users, command.UserId, ct) is not { } user)
            return Outcome<PersonDto>.NotFound("No person has that id.");
        if (await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
            return Outcome<PersonDto>.Forbidden("The owner account cannot be disabled or enabled.");

        if (command.Disabled)
        {
            LockedAccount.Require(await users.SetLockoutEnabledAsync(user, true));
            LockedAccount.Require(await users.SetLockoutEndDateAsync(user, AccountStatus.DisabledUntil));
            // D165: a link issued before the disable must not work again after a later enable.
            var revokedLinks = await resets.RevokeOpenAsync(user.Id, null, ct);
            await audit.WriteAsync(AuditActions.Disabled, command.ActorId, user.Id, revokedLinks > 0 ? new { resetLinksRevoked = revokedLinks } : null, ct);
            await sessions.EndAllAsync(user, ct);
            await revocation.MarkAsync(user.Id, ct);
        }
        else
        {
            LockedAccount.Require(await users.SetLockoutEndDateAsync(user, null));
            LockedAccount.Require(await users.ResetAccessFailedCountAsync(user));
            await audit.WriteAsync(AuditActions.Enabled, command.ActorId, user.Id, null, ct);
        }
        await transaction.CommitAsync(ct);

        logger.LogWarning("Account {UserId} {Action} by {ActorUserId} from {RemoteIp}",
            user.Id, command.Disabled ? "disabled" : "enabled", command.ActorId, client.Current);
        try
        {
            await revocation.SettleAsync(user.Id, command.Disabled ? "account disabled" : PrivilegeRevocation.Retried, command.ActorId,
                PrivilegeRevocation.SaveAgain, always: command.Disabled);
        }
        catch (RevocationFailedException ex)
        {
            var verb = command.Disabled ? "disable" : "enable";
            throw new RevocationFailedException($"Account {verb}d. {ex.Message.TrimEnd('.')} — {verb} again to retry.", ex.InnerException);
        }
        return Outcome<PersonDto>.Ok(await accounts.ToPersonAsync(user));
    }
}
