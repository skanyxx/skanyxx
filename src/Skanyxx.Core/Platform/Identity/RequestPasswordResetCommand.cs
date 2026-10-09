using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// "Forgot your password?" (D156): anonymous. Always the same answer, whatever the email; a reset link is emailed in
/// the background, and only to an account that may reset its password. Refused (404) only when SMTP is not configured.
/// </summary>
public sealed record RequestPasswordResetCommand(string Email) : IRequest<Outcome<bool>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ***");
        return true;
    }
}
