using System.Security.Claims;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// How memory's agent-secret scheme (D080) marks its principal. In Core so <see cref="LibraryUser.From"/> can refuse an
/// agent without referencing the module.
/// </summary>
public static class AgentPrincipal
{
    public const string SchemeName = "MemoryAgentSecret";

    /// <summary>D084: <c>"true"</c> when the agent may name a user in <c>X-User-Id</c>; otherwise it is ignored.</summary>
    public const string ActsForUsersClaim = "skanyxx:memory:acts_for_users";

    /// <summary>D076: memory's per-sandbox token scheme, accepted only on the sandbox listener (D077).</summary>
    public const string SandboxSchemeName = "MemorySandboxToken";

    /// <summary>Either memory scheme: a principal that is an agent or a sandbox, never a person.</summary>
    public static bool IsAgent(ClaimsPrincipal principal) =>
        principal.Identities.Any(i => i.AuthenticationType is SchemeName or SandboxSchemeName)
        || principal.HasClaim(c => c.Type == ActsForUsersClaim);
}
