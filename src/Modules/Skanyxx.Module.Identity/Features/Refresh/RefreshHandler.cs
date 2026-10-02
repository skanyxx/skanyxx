using MediatR;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Refresh;

/// <summary>
/// A refresh token is honoured once, while the user's security stamp is unchanged and its chain is inside the session
/// cap; the answer carries the chain's next token. Sign-out (which rotates the stamp and drops the chains) or a
/// password change ends every refresh token issued before it. The principal is rebuilt from the store, so role
/// changes apply at the next refresh. A refresh token is a password session, so an Entra-managed account's is refused
/// while Microsoft sign-in is enabled (D8, D14) — logged at Warning, it is someone still using an old token — and
/// honoured again once it is off.
/// </summary>
internal sealed class RefreshHandler(
    SignInManager<IdentityUser> signIn, BearerTokens tokens, RefreshChains chains, EntraPasswordRule entra, ClientAddress client, TimeProvider time,
    ILogger<RefreshHandler> logger)
    : IRequestHandler<RefreshCommand, Outcome<AccessTokenResponse>>
{
    private const string Refused = "The refresh token is invalid or expired; sign in again.";

    public async Task<Outcome<AccessTokenResponse>> Handle(RefreshCommand command, CancellationToken ct)
    {
        if (tokens.ReadRefresh(command.RefreshToken) is not var (principal, presented)
            || await signIn.ValidateSecurityStampAsync(principal) is not { } user)
            return Outcome<AccessTokenResponse>.Unauthorized(Refused);
        if (await entra.RefusesAsync(user, ct))
        {
            logger.LogWarning("Refresh of {UserId} from {RemoteIp} refused: the account signs in with Microsoft (D8)", user.Id, client.Current);
            return Outcome<AccessTokenResponse>.Forbidden(EntraPasswordRule.Message);
        }
        if (await chains.AdvanceAsync(user.Id, presented, time.GetUtcNow(), ct) is not { } next)
            return Outcome<AccessTokenResponse>.Unauthorized(Refused);

        return Outcome<AccessTokenResponse>.Ok(tokens.Issue(await signIn.CreateUserPrincipalAsync(user), next));
    }
}
