using Skanyxx.Core.Models;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>
/// <paramref name="Context"/> is prepended to the first message ("" when nothing related was found);
/// <paramref name="Sessions"/> and <paramref name="Cards"/> are exactly what it contains. A non-null
/// <paramref name="Cached"/> answers the turn without kagent; otherwise a non-null <paramref name="CacheKey"/> is where
/// the answer is to be kept (handed back in <see cref="ChatTurnDone"/>).
/// </summary>
public sealed record ChatPreparation(
    string Context, IReadOnlyList<RelatedSession> Sessions, IReadOnlyList<RelatedCard> Cards,
    ChatResponse? Cached = null, string? CacheKey = null)
{
    public static readonly ChatPreparation None = new("", [], []);
}
