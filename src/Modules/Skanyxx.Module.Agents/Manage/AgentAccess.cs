using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage;

/// <summary>
/// Who may do what on the Agents page (D254, D312). Supervisors (and the owner) change live agents; builders read every
/// agent; anyone else reads the agents Chat offers them, without their system prompt. Whoever may not change a live agent
/// submits the same form as a Studio proposal.
/// </summary>
internal static class AgentAccess
{
    public const string NotWriter = "Only a supervisor or the owner creates, edits or deletes live agents. Submit the form as a proposal instead.";

    public const string Writer = "You change agents directly: save the form instead of proposing it.";

    public static bool CanWrite(StudioUser user) => user.IsSupervisor;

    /// <summary>D312: builders and employees propose from the Agents page; supervisors save.</summary>
    public static bool CanProposeHere(StudioUser user) => !user.IsSupervisor;

    public static bool CanReadAll(StudioUser user) => user.IsSupervisor || user.IsBuilder;

    /// <summary>An employee reads what Chat offers them: a chat agent (D557) that is not suspended (D257).</summary>
    public static bool CanRead(StudioUser user, bool inChat, bool suspended) => CanReadAll(user) || (inChat && !suspended);
}
