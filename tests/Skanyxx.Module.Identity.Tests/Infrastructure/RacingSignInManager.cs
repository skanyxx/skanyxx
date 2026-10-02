using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Identity's sign-in manager with <see cref="SignInRace"/> run between the verified user and its cookie.</summary>
public sealed class RacingSignInManager(
    UserManager<IdentityUser> users, IHttpContextAccessor context, IUserClaimsPrincipalFactory<IdentityUser> claims,
    IOptions<IdentityOptions> options, ILogger<SignInManager<IdentityUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<IdentityUser> confirmation, SignInRace race)
    : SignInManager<IdentityUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override async Task SignInWithClaimsAsync(IdentityUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        await race.RunOnceAsync();
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
    }
}
