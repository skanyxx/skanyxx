using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// The owner invites <paramref name="Email"/> with <paramref name="Roles"/> (any of <see cref="SkanyxxRoles.Grantable"/>).
/// Refused when an account already has the email; an older pending invite for it is revoked (D026).
/// </summary>
public sealed record CreateInviteCommand(string ActorId, string Email, IReadOnlyList<string> Roles) : IRequest<Outcome<InviteIssued>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ActorId = {ActorId}, Email = ***, Roles = [{string.Join(", ", Roles)}]");
        return true;
    }
}
