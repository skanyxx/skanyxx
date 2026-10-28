using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage;

/// <summary>
/// Who may do what on the Agents page (D254, requirements.md §2, §3.3, O8). Supervisors (and the owner) change agents;
/// builders read every agent but change them only by Studio proposals; anyone else reads the agents Chat offers, without
/// their system prompt.
/// </summary>
internal static class AgentAccess
{
    public const string NotWriter = "Only a supervisor or the owner creates, edits or deletes agents. Builders propose agents in Studio.";

    public static bool CanWrite(StudioUser user) => user.IsSupervisor;

    public static bool CanReadAll(StudioUser user) => user.IsSupervisor || user.IsBuilder;

    public static bool CanRead(StudioUser user, bool inChat) => CanReadAll(user) || inChat;
}
