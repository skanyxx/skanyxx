using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Core.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatMessage Message { get; set; } = new();

    /// <summary>Prior chats injected into this (first) turn as context (D238); empty on later turns.</summary>
    public IReadOnlyList<RelatedSession> RelatedSessions { get; set; } = [];

    /// <summary>Memory cards injected into this (first) turn as context (D238).</summary>
    public IReadOnlyList<RelatedCard> RelatedCards { get; set; } = [];

    /// <summary>Served from the answer cache (D241): kagent was not asked, <see cref="ConversationId"/> is the original's.</summary>
    public bool Cached { get; set; }
}
