using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>A conversation as reopened: kagent's session is the source of truth, read as its owner.</summary>
internal sealed record ConversationView(
    string Id, string Title, string AgentNamespace, string AgentName, IReadOnlyList<ConversationItem> Items, ChatUsage Usage);
