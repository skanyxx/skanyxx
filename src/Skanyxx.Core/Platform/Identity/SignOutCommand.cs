using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Ends every session of the user, on every device: rotates the security stamp (cookies die at their next stamp
/// check, refresh tokens at once), drops the refresh chains and clears this browser's cookie. An access token already
/// issued stays valid until it expires. Contract lives in Core so the Host's logout page can send it.
/// </summary>
public sealed record SignOutCommand(string UserId) : IRequest<Outcome<bool>>;
