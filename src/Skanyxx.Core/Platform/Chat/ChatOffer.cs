using Skanyxx.Core.Models;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// Which kagent agents people may talk to (D557): every Agent kagent lists, wherever it came from (Studio, the Agents
/// page, the kagent UI, kubectl, Helm), except Skanyxx's own machinery and a suspended agent (D257). Chat's picker and
/// every chat route, Investigate's steps and the Agents page's "In Chat" all ask this one rule, so they never disagree.
/// The <c>skanyxx.dev/merged</c> label still means "came through review" (Studio), but no longer gates Chat.
/// An agent is <c>namespace/name</c>: two may share a name.
/// </summary>
public static class ChatOffer
{
    /// <summary>On a Studio preview agent: the pull request it previews (D030). It is chatted with in Studio only.</summary>
    public const string PreviewLabel = "skanyxx.dev/preview";

    /// <summary>On an agent Skanyxx runs for itself, such as the Studio factory (D5).</summary>
    public const string RoleLabel = "skanyxx.dev/role";

    public const string PartOfLabel = "app.kubernetes.io/part-of";

    /// <summary>The Tickets pipeline's stage agents (<c>deploy/kagent/ticket-flow/</c>): Skanyxx calls them, people do not.</summary>
    public const string TicketFlow = "skanyxx-ticket-flow";

    /// <summary>Whether Chat offers an agent with these labels while it is not suspended.</summary>
    public static bool IsChatAgent(IReadOnlyDictionary<string, string> labels) =>
        !labels.ContainsKey(PreviewLabel) && !labels.ContainsKey(RoleLabel) && labels.GetValueOrDefault(PartOfLabel) != TicketFlow;

    /// <summary>Whether Chat offers it now: a chat agent that is not suspended.</summary>
    public static bool Offers(Agent agent) => !agent.Suspended && IsChatAgent(agent.Labels);
}
