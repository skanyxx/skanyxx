using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// The Microsoft sign-in's second half (D1–D4). The account is found by its <c>tid|oid</c> login only, never by email.
/// A token that cannot tell a guest from a member (no <c>acct</c>, D9) is refused with nothing changed. No mapped group,
/// or a guest: refused, and an existing Entra-managed account loses its roles, teams and sessions (D2). The owner never
/// signs in with Microsoft (D11). Otherwise an existing account is re-mapped, a new one is created unless its email is
/// taken (D3), and a cookie session starts as after a password sign-in (the stamp checks apply to it).
/// </summary>
internal sealed class CompleteEntraSignInHandler(
    ExternalLogins logins, EntraSettingsCache cache, EntraMapper mapper, EntraAccounts accounts, UserManager<IdentityUser> users,
    SessionIssuer sessions, ClientAddress client, ILogger<CompleteEntraSignInHandler> logger)
    : IRequestHandler<CompleteEntraSignInCommand, Outcome<SignedIn>>
{
    public async Task<Outcome<SignedIn>> Handle(CompleteEntraSignInCommand command, CancellationToken ct)
    {
        var info = await logins.TakeAsync(expectedXsrf: null);
        var config = cache.Current;
        if (info?.LoginProvider != EntraScheme.Name || !config.CanSignIn)
            return Outcome<SignedIn>.Unauthorized(EntraSignInMessages.Incomplete);

        if (await EntraSignIn.MapAsync(mapper, info, config, logger, client, ct) is not { } mapping)
            return Outcome<SignedIn>.Unavailable(EntraSignInMessages.GraphUnavailable);
        if (EntraSignIn.RefuseUnknownKind(mapping, info, logger, client) is { } unknown)
            return unknown;

        var existing = await users.FindByLoginAsync(EntraScheme.Name, info.ProviderKey);
        if (existing is not null && await users.IsInRoleAsync(existing, SkanyxxRoles.Owner))
        {
            logger.LogWarning("Microsoft sign-in {Key} from {RemoteIp} refused: it is linked to the owner, who signs in with a password only",
                info.ProviderKey, client.Current);
            return Outcome<SignedIn>.Forbidden(EntraAccounts.OwnerStaysLocal);
        }
        if (!mapping.GrantsAccess)
        {
            if (existing is not null)
                await accounts.RefuseAsync(existing.Id, info.ProviderKey, mapping.RefusalReason, ct);
            else
                logger.LogWarning("Microsoft sign-in {Key} from {RemoteIp} refused: {Reason}", info.ProviderKey, client.Current, mapping.RefusalReason);
            return Outcome<SignedIn>.Forbidden(EntraSignInMessages.NoAccess);
        }

        if (existing is null)
        {
            var created = await accounts.CreateAsync(info, mapping, ct);
            if (created.Status != OutcomeStatus.Ok)
                return created.Value is { } user
                    ? new Outcome<SignedIn>(created.Status, await sessions.StartAsync(user, useCookie: true, ct))
                    : new Outcome<SignedIn>(created.Status, Message: created.Message);
            // Another callback bound this Microsoft account while this one waited: an existing account from here on.
            existing = created.Value!;
        }

        if (await accounts.SyncAsync(existing.Id, info.ProviderKey, mapping, ct) is not { } synced)
            return Outcome<SignedIn>.Forbidden(EntraSignInMessages.NoAccess);
        return Outcome<SignedIn>.Ok(await sessions.StartAsync(synced, useCookie: true, ct));
    }
}
