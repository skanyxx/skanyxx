using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Core.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatMessage Message { get; set; } = new();

    /// <summary>The memory cards given to the agent with this turn's message (a new chat's first turn only).</summary>
    public IReadOnlyList<MemoryCardRef> MemoryCards { get; set; } = [];

    /// <summary>Prior chats injected into this (first) turn as context (D238); empty on later turns.</summary>
    public IReadOnlyList<RelatedSession> RelatedSessions { get; set; } = [];

    /// <summary>Memory cards injected into this (first) turn as context (D238).</summary>
    public IReadOnlyList<RelatedCard> RelatedCards { get; set; } = [];

    /// <summary>Served from the answer cache (D241): kagent was not asked, <see cref="ConversationId"/> is the original's.</summary>
    public bool Cached { get; set; }
}
