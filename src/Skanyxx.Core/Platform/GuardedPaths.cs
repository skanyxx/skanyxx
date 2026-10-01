using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Routes that are origin-guarded for every method and rate limited: the new-module APIs (cookie or bearer), identity,
/// and <c>/mcp</c> (per-agent secrets, D080). Unsafe methods are origin-checked everywhere
/// (<see cref="OriginGuardMiddleware"/>).
/// </summary>
public static class GuardedPaths
{
    private static readonly PathString[] Prefixes = ["/api/memory", "/api/tickets", "/api/sandboxes", "/api/identity", "/mcp"];

    /// <summary>
    /// Where Microsoft Entra ID posts the sign-in result (OIDC <c>form_post</c>): a cross-site POST, so its <c>Origin</c>
    /// is Microsoft's (or <c>null</c>) and the origin check cannot apply. The OIDC handler protects it instead: the
    /// <c>state</c> must decrypt with this app's keys and match the correlation cookie, the id token's nonce must match
    /// the nonce cookie, and the code is only redeemable with the PKCE verifier kept in that state.
    /// </summary>
    public const string ExternalSignInCallback = "/signin-oidc";

    // Every POST that checks a password or a bootstrap token, through the API or the Razor forms, and the Microsoft
    // sign-in callback. Sign-out and refresh are not here: they stay in the general API window, so a burst of sign-ins
    // cannot stop anyone signing out.
    private static readonly PathString[] CredentialPaths =
        ["/api/identity/sign-in", "/api/identity/bootstrap", "/api/identity/unlock", "/Login", "/Setup", ExternalSignInCallback];

    // "Sign in with Microsoft" on /Login checks nothing (it only redirects to Microsoft); its callback is what counts,
    // so one Microsoft sign-in spends one permit, not two (SEC L2).
    private const string MicrosoftChallenge = "Microsoft";

    // The Account page's link re-checks the password (D10); only that form counts, not every POST the page may gain.
    private const string AccountPage = "/Account";
    private const string LinkMicrosoft = "LinkMicrosoft";

    private static readonly PathString[] InviteApiPaths = ["/api/identity/invites/lookup", "/api/identity/invites/accept"];

    // The accept page looks its token up on GET as well.
    private const string InvitePage = "/Invite";

    public static bool Contains(PathString path) => Prefixes.Any(path.StartsWithSegments);

    /// <summary>Exempt from the origin check (<see cref="ExternalSignInCallback"/>); exact path only.</summary>
    public static bool IsExternalSignInCallback(PathString path) => path.Equals(ExternalSignInCallback, StringComparison.OrdinalIgnoreCase);

    /// <summary>Counted in their own, stricter window (<see cref="SkanyxxOptions.SignInRateLimit"/>).</summary>
    public static bool IsCredentialPost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && ((CredentialPaths.Any(request.Path.StartsWithSegments)
                && !(request.Path.StartsWithSegments("/Login") && string.Equals(request.Query["handler"], MicrosoftChallenge, StringComparison.OrdinalIgnoreCase)))
            // Any of the values: a repeated handler parameter must not slip the link past the window.
            || (request.Path.StartsWithSegments(AccountPage)
                && request.Query["handler"].Any(h => string.Equals(h, LinkMicrosoft, StringComparison.OrdinalIgnoreCase))));

    /// <summary>Anything that checks an invite token, in <see cref="SkanyxxOptions.InviteRateLimit"/>.</summary>
    public static bool IsInviteRequest(HttpRequest request) =>
        (HttpMethods.IsPost(request.Method) && InviteApiPaths.Any(request.Path.StartsWithSegments))
        || request.Path.StartsWithSegments(InvitePage);
}
