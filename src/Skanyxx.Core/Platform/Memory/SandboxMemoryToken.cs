using System.Text;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>A freshly signed sandbox token, for the sandbox's env only. Never printed.</summary>
public sealed record SandboxMemoryToken(string AgentId, string Token, DateTimeOffset ExpiresAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"AgentId = {AgentId}, Token = ***, ExpiresAt = {ExpiresAt:O}");
        return true;
    }
}
