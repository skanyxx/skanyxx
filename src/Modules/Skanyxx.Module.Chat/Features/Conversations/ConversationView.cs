using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// A conversation as reopened: kagent's session is the source of truth, read as its owner. <paramref name="Usage"/> is
/// the model's tokens; <paramref name="ClientTokens"/> Skanyxx's own count of what it sent (D548), never added to them.
/// </summary>
internal sealed record ConversationView(
    string Id, string Title, string AgentNamespace, string AgentName, IReadOnlyList<ConversationItem> Items, ChatUsage Usage, long ClientTokens);
