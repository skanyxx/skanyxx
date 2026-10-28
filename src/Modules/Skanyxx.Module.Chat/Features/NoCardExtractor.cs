using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features;

/// <summary>Without the memory module a chat leaves no card behind.</summary>
internal sealed class NoCardExtractor : ICardExtractor
{
    public Task<MemoryCardRef?> ChatTurnAsync(ChatTurnDigest turn, CancellationToken ct) => Task.FromResult<MemoryCardRef?>(null);

    public Task<MemoryCardRef?> TicketRunAsync(TicketRunFinished run, CancellationToken ct) => Task.FromResult<MemoryCardRef?>(null);
}
