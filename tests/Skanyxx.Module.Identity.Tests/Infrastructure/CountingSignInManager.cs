using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Identity's sign-in manager, counting the security stamp checks (each one is a database read).</summary>
public sealed class CountingSignInManager(
    UserManager<IdentityUser> users, IHttpContextAccessor context, IUserClaimsPrincipalFactory<IdentityUser> claims,
    IOptions<IdentityOptions> options, ILogger<SignInManager<IdentityUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<IdentityUser> confirmation, StampChecks checks)
    : SignInManager<IdentityUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override Task<IdentityUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        Interlocked.Increment(ref checks.Count);
        return base.ValidateSecurityStampAsync(principal);
    }
}
