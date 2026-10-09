using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>
/// Undoes a link (D10): drops the <c>entra</c> login, rotates the stamp and revokes the refresh chains in the same
/// transaction, so a Microsoft identity linked by someone else stops reaching the account and nothing signed in keeps
/// going. Audited at Warning. Refused for an account without a password (D16): Microsoft is its only sign-in, so
/// removing it would leave an account nobody can ever reach again; disabling is the reversible way to stop it.
/// </summary>
internal sealed class RemoveEntraLoginHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionRevocation sessions, AccountReader accounts, IdentityAudit audit,
    ClientAddress client, ILogger<RemoveEntraLoginHandler> logger)
    : IRequestHandler<RemoveEntraLoginCommand, Outcome<PersonDto>>
{
    public const string NotLinked = "This person has no Microsoft login.";

    public const string OnlySignIn = "Microsoft is this account's only sign-in; disable it instead.";

    public async Task<Outcome<PersonDto>> Handle(RemoveEntraLoginCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockedAccount.LoadAsync(db, users, command.UserId, ct) is not { } user)
            return Outcome<PersonDto>.NotFound("No person has that id.");
        if ((await users.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == EntraScheme.Name) is not { } login)
            return Outcome<PersonDto>.NotFound(NotLinked);
        if (!await users.HasPasswordAsync(user))
            return Outcome<PersonDto>.Conflict(null, OnlySignIn);

        LockedAccount.Require(await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey));
        await EntraRefusal.ClearAsync(db, user.Id, ct);
        await audit.WriteAsync(AuditActions.EntraLoginRemoved, command.ActorId, user.Id, new { key = login.ProviderKey }, ct);
        await sessions.EndAllAsync(user, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Microsoft login {Key} removed from {UserId} by {ActorUserId} from {RemoteIp}; the account is local-only and its sessions ended",
            login.ProviderKey, user.Id, command.ActorId, client.Current);
        return Outcome<PersonDto>.Ok(await accounts.ToPersonAsync(user));
    }
}
