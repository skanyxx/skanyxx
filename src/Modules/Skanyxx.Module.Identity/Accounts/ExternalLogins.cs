using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>The external-login cookie an OIDC callback leaves behind, read once and then dropped. Keeps handlers off the HTTP types.</summary>
internal sealed class ExternalLogins(SignInManager<IdentityUser> signIn)
{
    /// <summary>
    /// The pending external sign-in, or null (none, expired, or — with <paramref name="expectedXsrf"/> — not started by
    /// that user). The cookie is dropped either way: a result is used at most once.
    /// </summary>
    public async Task<ExternalLoginInfo?> TakeAsync(string? expectedXsrf)
    {
        var info = await signIn.GetExternalLoginInfoAsync(expectedXsrf);
        await signIn.Context.SignOutAsync(IdentityConstants.ExternalScheme);
        return info;
    }

    /// <summary>Challenge properties coming back to <paramref name="completionPath"/>; with <paramref name="linkUserId"/>, bound to that user (XSRF).</summary>
    public AuthenticationProperties Challenge(string provider, string completionPath, string? linkUserId) =>
        signIn.ConfigureExternalAuthenticationProperties(provider, completionPath, linkUserId);
}
