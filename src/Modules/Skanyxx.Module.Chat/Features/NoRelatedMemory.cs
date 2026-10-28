using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features;

/// <summary>Without the memory module a new chat starts with no related cards.</summary>
internal sealed class NoRelatedMemory : IRelatedMemory
{
    public Task<MemoryContext> ForNewChatAsync(RelatedMemoryQuery query, CancellationToken ct) => Task.FromResult(MemoryContext.None);
}
