using MediatR;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Features.Refresh;

/// <summary>
/// A refresh token is honoured once, while the user's security stamp is unchanged and its chain is inside the session
/// cap; the answer carries the chain's next token. Sign-out (which rotates the stamp and drops the chains) or a
/// password change ends every refresh token issued before it. The principal is rebuilt from the store, so role
/// changes apply at the next refresh.
/// </summary>
internal sealed class RefreshHandler(SignInManager<IdentityUser> signIn, BearerTokens tokens, RefreshChains chains, TimeProvider time)
    : IRequestHandler<RefreshCommand, Outcome<AccessTokenResponse>>
{
    private const string Refused = "The refresh token is invalid or expired; sign in again.";

    public async Task<Outcome<AccessTokenResponse>> Handle(RefreshCommand command, CancellationToken ct)
    {
        if (tokens.ReadRefresh(command.RefreshToken) is not var (principal, presented)
            || await signIn.ValidateSecurityStampAsync(principal) is not { } user
            || await chains.AdvanceAsync(user.Id, presented, time.GetUtcNow(), ct) is not { } next)
            return Outcome<AccessTokenResponse>.Unauthorized(Refused);

        return Outcome<AccessTokenResponse>.Ok(tokens.Issue(await signIn.CreateUserPrincipalAsync(user), next));
    }
}
