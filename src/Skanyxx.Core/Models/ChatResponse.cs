using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Core.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatMessage Message { get; set; } = new();

    /// <summary>The memory cards given to the agent with this turn's message (a new chat's first turn only).</summary>
    public IReadOnlyList<MemoryCardRef> MemoryCards { get; set; } = [];
}
