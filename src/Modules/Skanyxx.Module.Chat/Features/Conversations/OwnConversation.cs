using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// The caller's own conversation, or none: kagent is asked as the caller, and the session must also name them and be a
/// person's chat (not a session a parent agent opened on a sub-agent). Anyone else's id is "not found", the owner's too.
/// </summary>
internal static class OwnConversation
{
    public const string NotFound = "No such conversation.";

    public const string Untitled = "New chat";

    public static bool IsOwn(KAgentSession? session, string userId) =>
        session is not null && session.UserId == userId && session.Source != "agent" && MergedAgents.FromSessionAgentId(session.AgentId) is not null;

    public static async Task<KAgentSession?> FindAsync(KAgentApiClient kagent, string userId, string conversationId, CancellationToken ct) =>
        await kagent.GetSessionAsync(conversationId, userId, ct) is { } session && IsOwn(session, userId) ? session : null;

    public static string Title(KAgentSession session) => string.IsNullOrWhiteSpace(session.Name) ? Untitled : session.Name;
}
