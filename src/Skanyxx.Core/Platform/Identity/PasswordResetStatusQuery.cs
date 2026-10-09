using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>What the reset page shows for a pending link; one 404 for unknown, used, revoked and expired.</summary>
public sealed record PasswordResetStatusQuery(string Token) : IRequest<Outcome<PasswordResetDetails>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ***");
        return true;
    }
}
