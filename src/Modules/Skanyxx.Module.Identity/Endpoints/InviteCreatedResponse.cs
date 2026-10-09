using System.Text;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>
/// <see cref="Link"/> carries the invite token; this response is the only place it ever appears, and only when it was
/// not emailed to the invitee (<see cref="Emailed"/>, D151).
/// </summary>
public sealed record InviteCreatedResponse(string InviteId, string? Link, DateTimeOffset ExpiresAt, bool Emailed)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"InviteId = {InviteId}, Link = ***, ExpiresAt = {ExpiresAt:O}, Emailed = {Emailed}");
        return true;
    }
}
