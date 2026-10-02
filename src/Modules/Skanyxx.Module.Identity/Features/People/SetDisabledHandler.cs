using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>
/// Disable = locked out until <see cref="AccountStatus.DisabledUntil"/> + every session ended; enable = lockout and
/// failed count cleared. The owner is never disabled: there is no one left to enable them. Every disable (a repeat
/// one included, which makes it the retry) also revokes, after the commit, the durable credentials the person issued
/// (<see cref="PrivilegeRevocation"/>); enabling does not bring them back. A disable marks the revocation as owed in its
/// transaction (D13): a failed publish is retried by the next disable, enable, role save or Microsoft sign-in.
/// </summary>
internal sealed class SetDisabledHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionRevocation sessions, AccountReader accounts, PrivilegeRevocation revocation,
    ClientAddress client, ILogger<SetDisabledHandler> logger)
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
            await sessions.EndAllAsync(user, ct);
            await revocation.MarkAsync(user.Id, ct);
        }
        else
        {
            LockedAccount.Require(await users.SetLockoutEndDateAsync(user, null));
            LockedAccount.Require(await users.ResetAccessFailedCountAsync(user));
        }
        await transaction.CommitAsync(ct);

        logger.LogWarning("Account {UserId} {Action} by {ActorUserId} from {RemoteIp}",
            user.Id, command.Disabled ? "disabled" : "enabled", command.ActorId, client.Current);
        await revocation.SettleAsync(user.Id, command.Disabled ? "account disabled" : PrivilegeRevocation.Retried, command.ActorId,
            PrivilegeRevocation.SaveAgain, always: command.Disabled);
        return Outcome<PersonDto>.Ok(await accounts.ToPersonAsync(user));
    }
}
