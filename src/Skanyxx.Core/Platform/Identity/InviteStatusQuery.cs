using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// What a pending invite offers, for the accept page. Unknown, used, revoked and expired tokens get one and the same
/// not-found answer.
/// </summary>
public sealed record InviteStatusQuery(string Token) : IRequest<Outcome<InviteDetails>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ***");
        return true;
    }
}
