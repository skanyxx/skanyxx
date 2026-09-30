using System.Text;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// <see cref="Link"/> carries the token and exists only here: the store keeps its hash. Shown to the inviter once.
/// </summary>
public sealed record InviteIssued(string InviteId, string Link, DateTimeOffset ExpiresAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"InviteId = {InviteId}, Link = ***, ExpiresAt = {ExpiresAt:O}");
        return true;
    }
}
