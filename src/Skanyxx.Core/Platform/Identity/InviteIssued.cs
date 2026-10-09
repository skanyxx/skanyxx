using System.Text;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// <see cref="Link"/> carries the token and exists only here: the store keeps its hash. Shown to the inviter once, and
/// only when it was not emailed to the invitee (<see cref="Emailed"/>, D151): SMTP is off, or sending failed.
/// </summary>
public sealed record InviteIssued(string InviteId, string? Link, DateTimeOffset ExpiresAt, bool Emailed)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"InviteId = {InviteId}, Link = ***, ExpiresAt = {ExpiresAt:O}, Emailed = {Emailed}");
        return true;
    }
}
