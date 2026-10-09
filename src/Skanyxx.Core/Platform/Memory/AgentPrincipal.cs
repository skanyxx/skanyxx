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
}
