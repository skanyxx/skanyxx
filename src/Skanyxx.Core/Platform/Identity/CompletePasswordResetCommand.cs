using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Spends the reset link (once) and sets <paramref name="Password"/>: every session of the account ends, its lockout and
/// failed count are cleared. No session is started: the person signs in with the new password.
/// </summary>
public sealed record CompletePasswordResetCommand(string Token, string Password) : IRequest<Outcome<bool>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ***, Password = ***");
        return true;
    }
}
