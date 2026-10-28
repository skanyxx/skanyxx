using Skanyxx.Core.Models;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>
/// <paramref name="Context"/> is prepended to the first message ("" when nothing related was found);
/// <paramref name="Sessions"/> are exactly what it contains (cards come from memory's <c>IRelatedMemory</c>). A non-null
/// <paramref name="Cached"/> answers the turn without kagent; otherwise a non-null <paramref name="CacheKey"/> is where
/// the answer is to be kept (handed back in <see cref="ChatTurnDone"/>).
/// </summary>
public sealed record ChatPreparation(
    string Context, IReadOnlyList<RelatedSession> Sessions,
    ChatResponse? Cached = null, string? CacheKey = null)
{
    public static readonly ChatPreparation None = new("", []);
}
