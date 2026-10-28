using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Core.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatMessage Message { get; set; } = new();

    /// <summary>The memory cards given to the agent with this turn's message (a new chat's first turn only).</summary>
    public IReadOnlyList<MemoryCardRef> MemoryCards { get; set; } = [];

    /// <summary>Prior chats injected into this (first) turn as context (D262); empty on later turns.</summary>
    public IReadOnlyList<RelatedSession> RelatedSessions { get; set; } = [];

    /// <summary>Served from the answer cache (D265): kagent was not asked, <see cref="ConversationId"/> is the original's.</summary>
    public bool Cached { get; set; }

    /// <summary>The tokens kagent reported for this turn (none for a cached answer, or when the runtime reports none).</summary>
    public TokenUsage Usage { get; set; } = TokenUsage.None;
}
