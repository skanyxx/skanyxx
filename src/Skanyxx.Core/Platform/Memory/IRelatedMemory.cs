namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The published cards related to a new conversation, under a token budget (requirements.md O1, §3.4). Only cards the
/// person may read AND the agent may search: the block goes into that agent's prompt, so neither side's access may widen
/// the other's. Implemented by the memory module; without it, nothing is injected.
/// </summary>
public interface IRelatedMemory
{
    /// <summary>A memory failure answers <see cref="MemoryContext.None"/>: the chat goes on without it.</summary>
    Task<MemoryContext> ForNewChatAsync(RelatedMemoryQuery query, CancellationToken ct);
}
