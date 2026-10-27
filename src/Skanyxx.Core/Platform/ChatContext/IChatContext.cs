namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>
/// What Chat asks around a turn (D236): before the first turn of a new conversation, related prior work (and, when the
/// answer cache is on, a cached answer); after every turn, an update of the chat index. Implemented by the chatcontext
/// module; Chat registers a no-op so it keeps working without that module.
/// </summary>
public interface IChatContext
{
    /// <summary>An index or cache failure answers <see cref="ChatPreparation.None"/>: chat works without them.</summary>
    Task<ChatPreparation> PrepareAsync(ChatTurnStart start, CancellationToken ct);

    /// <summary>Never throws for an index or cache failure: the person already has their answer.</summary>
    Task CompletedAsync(ChatTurnDone done, CancellationToken ct);
}
