using System.Text;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>The only response that ever carries the plaintext secret; it is not stored anywhere, nor printed.</summary>
public sealed record IssuedAgentSecret(string AgentId, string Secret, DateTime CreatedAt, bool ActsForUsers)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"AgentId = {AgentId}, Secret = ***, CreatedAt = {CreatedAt:O}, ActsForUsers = {ActsForUsers}");
        return true;
    }
}
