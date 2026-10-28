namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Keeps finished work as one small card in its owner's personal scope (memory-hierarchy.md: cards are born personal,
/// then lifted). A chat session is one card, updated as the session goes on; a ticket run is one card. The card holds a
/// line of what and why, ticket keys and a pointer back (<c>chat:</c>/<c>run:</c>), never the transcript.
/// Implemented by the memory module; without it, nothing is kept.
/// </summary>
public interface ICardExtractor
{
    /// <returns>The session's card, or null when nothing was kept (too little said, a limit, or a memory failure).</returns>
    Task<MemoryCardRef?> ChatTurnAsync(ChatTurnDigest turn, CancellationToken ct);

    /// <returns>The run's card, or null when nothing was kept.</returns>
    Task<MemoryCardRef?> TicketRunAsync(TicketRunFinished run, CancellationToken ct);
}
