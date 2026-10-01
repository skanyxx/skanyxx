using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// <c>404</c> while Microsoft sign-in is off (D6); <c>409</c> outside Development without <c>Identity:PublicBaseUrl</c>.
/// A link is refused for the owner (D11), for an account already linked, and for one without a password (it could not
/// prove it, and its only sign-in is already Microsoft), before the password step-up counts anything; otherwise it
/// needs the account's current password (D10), checked before anything is sent to Microsoft.
/// </summary>
internal sealed class EntraChallengeHandler(
    EntraSettingsCache cache, EntraRedirectUri redirect, ExternalLogins logins, UserManager<IdentityUser> users, PasswordStepUp stepUp,
    ClientAddress client, ILogger<EntraChallengeHandler> logger)
    : IRequestHandler<EntraChallengeQuery, Outcome<EntraChallenge>>
{
    public const string Off = "Microsoft sign-in is not enabled.";

    public const string NoPassword = "This account has no password, so it cannot link a Microsoft account.";

    public async Task<Outcome<EntraChallenge>> Handle(EntraChallengeQuery query, CancellationToken ct)
    {
        if (!cache.Current.CanSignIn)
            return Outcome<EntraChallenge>.NotFound(Off);
        if (!redirect.CanBuild)
            return Outcome<EntraChallenge>.Conflict(null, EntraRedirectUri.NotConfigured);

        if (query.LinkUserId is { } userId)
        {
            if (await users.FindByIdAsync(userId) is not { } user)
                return Outcome<EntraChallenge>.Forbidden(PasswordStepUp.Wrong);
            if (await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
                return Outcome<EntraChallenge>.Forbidden(EntraAccounts.OwnerStaysLocal);
            if ((await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == EntraScheme.Name))
                return Outcome<EntraChallenge>.Conflict(null, EntraAccounts.AlreadyLinked);
            if (!await users.HasPasswordAsync(user))
                return Outcome<EntraChallenge>.Forbidden(NoPassword);
            if (await stepUp.CheckAsync(userId, query.LinkPassword!, ct) is { } refused)
            {
                logger.LogWarning("Microsoft link for {UserId} from {RemoteIp} not started: {Reason}", userId, client.Current, refused.Message);
                return new Outcome<EntraChallenge>(refused.Status, Message: refused.Message);
            }
        }
        return Outcome<EntraChallenge>.Ok(new EntraChallenge(EntraScheme.Name, logins.Challenge(EntraScheme.Name, query.CompletionPath, query.LinkUserId)));
    }
}
