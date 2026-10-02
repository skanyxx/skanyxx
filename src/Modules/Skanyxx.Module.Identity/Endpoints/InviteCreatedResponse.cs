using System.Text;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary><see cref="Link"/> carries the invite token; this response is the only place it ever appears.</summary>
public sealed record InviteCreatedResponse(string InviteId, string Link, DateTimeOffset ExpiresAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"InviteId = {InviteId}, Link = ***, ExpiresAt = {ExpiresAt:O}");
        return true;
    }
}
