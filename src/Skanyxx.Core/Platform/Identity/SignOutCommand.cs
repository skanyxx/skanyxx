using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Ends every session of the user, on every device: rotates the security stamp, drops the refresh chains and clears
/// this browser's cookie. Refresh tokens are refused at once, and cookies and access tokens already issued from their
/// next request (both carriers check the stamp on every request). Contract lives in Core so the
/// Host's logout page can send it.
/// </summary>
public sealed record SignOutCommand(string UserId) : IRequest<Outcome<bool>>;
