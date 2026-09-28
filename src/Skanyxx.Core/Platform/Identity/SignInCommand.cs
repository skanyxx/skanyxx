using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Password sign-in. <paramref name="UseCookie"/>: the browser session cookie is set; otherwise a bearer access token
/// and refresh token are returned (API and desktop clients, D059). <paramref name="BootstrapToken"/>: when it matches
/// <c>Identity:BootstrapToken</c> and the account is the owner, the lockout does not apply (break-glass, D081); the
/// password is still checked, and a wrong one adds to the failed count that sign-ins without the token are held to.
/// </summary>
public sealed record SignInCommand(string Email, string Password, bool UseCookie, string? BootstrapToken = null)
    : IRequest<Outcome<SignedIn>>;
