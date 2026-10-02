using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>Starts a session for a verified user: the browser cookie, or bearer + refresh tokens on a new chain.</summary>
internal sealed class SessionIssuer(
    SignInManager<IdentityUser> signIn, BearerTokens tokens, RefreshChains chains, AccountReader accounts, TimeProvider time)
{
    public async Task<SignedIn> StartAsync(IdentityUser user, bool useCookie, CancellationToken ct)
    {
        var account = await accounts.ToDtoAsync(user);
        var now = time.GetUtcNow();
        if (useCookie)
        {
            await signIn.SignInAsync(user, SessionStart.Stamp(now));
            return new SignedIn(account, null);
        }

        var chain = await chains.StartAsync(user.Id, now, ct);
        return new SignedIn(account, tokens.Issue(await signIn.CreateUserPrincipalAsync(user), chain));
    }
}
