using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>
/// Replaces the grantable roles of anyone but the owner, whose roles are fixed (the owner already counts as supervisor).
/// A real change ends the person's sessions (<see cref="SessionRevocation"/>) in the same transaction, so nothing
/// signed in keeps the old roles. Saving roles without supervisor (a no-op included, which makes re-saving the retry)
/// revokes, after the commit, what the person issued as one (<see cref="PrivilegeRevocation"/>). Taking supervisor away
/// also marks the revocation as owed in the same transaction (D13), so a failed publish is retried by the next save or
/// Microsoft sign-in of the account without supervisor. A save that leaves supervisor on drops any owed revocation (D17).
/// A real change is audited in the same transaction (D152). When the publish fails (a disabled or Entra-refused
/// account's sandbox tasks with AX down) the roles stay saved and the <see cref="RevocationFailedException"/> says so
/// and that saving again retries, as a disable does (D162).
/// </summary>
internal sealed class SetRolesHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionRevocation sessions, AccountReader accounts, PrivilegeRevocation revocation,
    IdentityAudit audit, ClientAddress client, ILogger<SetRolesHandler> logger)
    : IRequestHandler<SetRolesCommand, Outcome<PersonDto>>
{
    public const string OwnerFixed = "The owner's roles are fixed: the owner already has every permission a role grants.";

    public async Task<Outcome<PersonDto>> Handle(SetRolesCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockedAccount.LoadAsync(db, users, command.UserId, ct) is not { } user)
            return Outcome<PersonDto>.NotFound("No person has that id.");

        var current = await users.GetRolesAsync(user);
        if (current.Contains(SkanyxxRoles.Owner))
            return Outcome<PersonDto>.Forbidden(OwnerFixed);

        var wanted = command.Roles.Distinct().ToList();
        var removed = current.Except(wanted).ToList();
        var added = wanted.Except(current).ToList();
        if (removed.Count > 0 || added.Count > 0)
        {
            LockedAccount.Require(await users.RemoveFromRolesAsync(user, removed));
            LockedAccount.Require(await users.AddToRolesAsync(user, added));
            await audit.WriteAsync(AuditActions.RolesChanged, command.ActorId, user.Id, new { added, removed }, ct);
            await sessions.EndAllAsync(user, ct);
        }
        var withoutSupervisor = !wanted.Contains(SkanyxxRoles.Supervisor);
        if (removed.Contains(SkanyxxRoles.Supervisor))
            await revocation.MarkAsync(user.Id, ct);
        else if (!withoutSupervisor)
            await revocation.ForgiveAsync(user.Id, ct);
        await transaction.CommitAsync(ct);

        if (removed.Count > 0 || added.Count > 0)
            logger.LogWarning("Roles of {UserId} changed by {ActorUserId} from {RemoteIp}: added {Added}, removed {Removed}; sessions ended",
                user.Id, command.ActorId, client.Current, added, removed);
        try
        {
            await revocation.SettleAsync(user.Id, withoutSupervisor ? "roles saved without supervisor" : PrivilegeRevocation.Retried, command.ActorId,
                PrivilegeRevocation.SaveAgain, always: withoutSupervisor);
        }
        catch (RevocationFailedException ex)
        {
            throw new RevocationFailedException($"Roles saved. {ex.Message.TrimEnd('.')} — save again to retry.", ex.InnerException);
        }
        return Outcome<PersonDto>.Ok(await accounts.ToPersonAsync(user));
    }
}
