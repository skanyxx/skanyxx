using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// D3's safe path: the person signs in with their password, re-enters it to start the link (D10), and only a Microsoft result that carries
/// their own user id (Identity's XSRF check) is accepted — someone else's callback, or a sign-in started by nobody, is
/// not. The Microsoft account must be in a mapped group like any Microsoft sign-in; the account then becomes
/// Entra-managed and a fresh cookie session starts with the mapped roles.
/// </summary>
internal sealed class LinkEntraHandler(
    ExternalLogins logins, EntraSettingsCache cache, EntraMapper mapper, EntraAccounts accounts, SessionIssuer sessions, ClientAddress client,
    ILogger<LinkEntraHandler> logger)
    : IRequestHandler<LinkEntraCommand, Outcome<SignedIn>>
{
    public async Task<Outcome<SignedIn>> Handle(LinkEntraCommand command, CancellationToken ct)
    {
        var info = await logins.TakeAsync(expectedXsrf: command.UserId);
        var config = cache.Current;
        if (info?.LoginProvider != EntraScheme.Name || !config.CanSignIn)
        {
            logger.LogWarning("Microsoft link for {UserId} from {RemoteIp} refused: no Microsoft sign-in started by this user", command.UserId, client.Current);
            return Outcome<SignedIn>.Unauthorized(EntraSignInMessages.Incomplete);
        }

        if (await EntraSignIn.MapAsync(mapper, info, config, logger, client, ct) is not { } mapping)
            return Outcome<SignedIn>.Unavailable(EntraSignInMessages.GraphUnavailable);
        if (EntraSignIn.RefuseUnknownKind(mapping, info, logger, client) is { } unknown)
            return unknown;
        if (!mapping.GrantsAccess)
        {
            logger.LogWarning("Microsoft link of {UserId} to {Key} from {RemoteIp} refused: {Reason}", command.UserId, info.ProviderKey, client.Current, mapping.RefusalReason);
            return Outcome<SignedIn>.Forbidden(EntraSignInMessages.NoAccess);
        }

        var linked = await accounts.LinkAsync(command.UserId, info.ProviderKey, mapping, ct);
        return linked.Value is { } user
            ? Outcome<SignedIn>.Ok(await sessions.StartAsync(user, useCookie: true, ct))
            : new Outcome<SignedIn>(linked.Status, Message: linked.Message);
    }
}
